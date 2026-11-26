using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Lynx.Protocol;

namespace Lynx.Operator.Core;

public sealed record OperatorRelayClientOptions(
    string RelayHost = "auto",
    int RelayPort = 7777,
    TimeSpan? DiscoveryTimeout = null,
    TimeSpan? HeartbeatInterval = null,
    TimeSpan? SnapshotTimeout = null,
    TimeSpan? RetryDelay = null
);

public sealed class OperatorRelayClient : IAsyncDisposable
{
    private const int MaxPayloadBytes = 4096;

    private readonly AuthoritativePingState _state = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _socketLock = new();
    private readonly object _snapshotReceiptLock = new();

    private UdpClient? _socket;
    private IPEndPoint? _relayEndpoint;
    private long? _pendingSnapshotId;
    private int _pendingSnapshotChunkCount;
    private readonly HashSet<int> _receivedSnapshotChunks = new();
    private DateTimeOffset? _snapshotDeadlineUtc;

    public OperatorRelayClient(long clientId)
    {
        if (clientId <= 0)
            throw new ArgumentOutOfRangeException(nameof(clientId));
        ClientId = clientId;
    }

    public long ClientId { get; }

    public long AppliedEpoch => _state.AppliedEpoch;
    public long AppliedVersion => _state.AppliedVersion;

    public RelayEndpoint? CurrentRelay
    {
        get
        {
            lock (_socketLock)
            {
                return _relayEndpoint is null
                    ? null
                    : new RelayEndpoint(
                        _relayEndpoint.Address.ToString(),
                        _relayEndpoint.Port);
            }
        }
    }

    public event Action<string>? StatusChanged;
    public event Action<RelayEndpoint?>? RelayEndpointChanged;
    public event Action<IReadOnlyList<PingMessage>, long, long>? StateChanged;

    public IReadOnlyList<PingMessage> StateSnapshot() => _state.Snapshot();

    public async Task RunAsync(
        OperatorRelayClientOptions options,
        CancellationToken cancellationToken)
    {
        ValidateOptions(options);

        var discoveryTimeout = options.DiscoveryTimeout ?? TimeSpan.FromSeconds(2);
        var retryDelay = options.RetryDelay ?? TimeSpan.FromSeconds(2);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetStatus($"Resolving relay {options.RelayHost}:{options.RelayPort}...");
                var resolved = await RelayEndpointResolver.ResolveAsync(
                    options.RelayHost,
                    options.RelayPort,
                    discoveryTimeout,
                    cancellationToken);

                if (resolved is null)
                {
                    SetStatus("Relay not found; retrying.");
                    await Task.Delay(retryDelay, cancellationToken);
                    continue;
                }

                await RunConnectedSessionAsync(
                    resolved,
                    options,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus($"Network session failed: {ex.Message}. Retrying.");
                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        ClearSocket();
        SetStatus("Stopped.");
    }

    public async Task<PingMessage> CreateMarkerAsync(
        SharedPoint shared,
        int beaconId = 0,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(shared.X) ||
            !double.IsFinite(shared.Y) ||
            !double.IsFinite(shared.Z))
            throw new ArgumentOutOfRangeException(nameof(shared));

        var ping = new PingMessage(
            Id: CreatePositiveRandomId(),
            SenderId: ClientId,
            BeaconId: beaconId,
            X: checked((float)shared.X),
            Y: checked((float)shared.Y),
            Z: checked((float)shared.Z),
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await SendMessageAsync(
            LynxMessageCodec.CreatePing(ping),
            cancellationToken);

        return ping;
    }

    public async Task RemoveMarkerAsync(
        long pingId,
        CancellationToken cancellationToken = default)
    {
        if (!_state.TryGet(pingId, out var ping) || ping is null)
            throw new InvalidOperationException($"Ping {pingId} is not in authoritative state.");

        if (ping.SenderId != ClientId)
            throw new InvalidOperationException(
                "Prototype 0.5 can remove only markers owned by this operator client.");

        await SendMessageAsync(
            LynxMessageCodec.RemovePing(pingId, ClientId),
            cancellationToken);
    }

    public async Task RequestSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        _state.ResetPendingSnapshot();
        ResetSnapshotReceipt();
        await SendMessageAsync(
            LynxMessageCodec.RequestSnapshot(ClientId),
            cancellationToken);
    }

    private async Task RunConnectedSessionAsync(
        RelayEndpoint relay,
        OperatorRelayClientOptions options,
        CancellationToken cancellationToken)
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        socket.Client.ReceiveBufferSize = 64 * 1024;

        SetSocket(socket, relay.ToIPEndPoint());
        SetStatus($"Relay {relay.Host}:{relay.Port}; registering client {ClientId}.");
        await SendMessageAsync(
            LynxMessageCodec.RegisterClient(ClientId),
            cancellationToken);

        using var maintenanceCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var maintenanceTask = MaintenanceLoopAsync(
            options,
            maintenanceCts.Token);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try
                {
                    received = await socket.ReceiveAsync(cancellationToken);
                }
                catch (SocketException ex)
                {
                    SetStatus($"Transient UDP receive error {ex.SocketErrorCode}; retrying.");
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                if (received.Buffer.Length > MaxPayloadBytes)
                {
                    SetStatus("Dropped oversize UDP payload.");
                    continue;
                }

                if (!LynxMessageCodec.TryParse(
                        received.Buffer,
                        out var message,
                        out var error) ||
                    message is null)
                {
                    SetStatus($"Dropped invalid relay payload: {error}");
                    continue;
                }

                TrackSnapshotReceipt(message, options);

                var result = _state.Apply(message);
                switch (result.Status)
                {
                    case StateApplyStatus.ResyncRequired:
                        SetStatus(
                            $"State gap/conflict at {_state.AppliedEpoch}:{_state.AppliedVersion}; requesting snapshot.");
                        await RequestSnapshotAsync(cancellationToken);
                        break;

                    case StateApplyStatus.SnapshotIncomplete:
                        SetStatus(
                            $"Snapshot chunk {message.ChunkIndex!.Value + 1}/{message.ChunkCount} received.");
                        break;

                    case StateApplyStatus.SnapshotApplied:
                        ResetSnapshotReceipt();
                        SetStatus(
                            $"Snapshot applied: {_state.Snapshot().Count} marker(s), " +
                            $"epoch {_state.AppliedEpoch}, version {_state.AppliedVersion}.");
                        RaiseStateChanged();
                        break;

                    case StateApplyStatus.Applied:
                        SetStatus(
                            $"Authoritative update applied at " +
                            $"{_state.AppliedEpoch}:{_state.AppliedVersion}.");
                        RaiseStateChanged();
                        break;

                    case StateApplyStatus.StaleIgnored:
                        SetStatus("Stale/duplicate authoritative packet ignored.");
                        break;
                }
            }
        }
        finally
        {
            maintenanceCts.Cancel();
            try
            {
                await maintenanceTask;
            }
            catch (OperationCanceledException)
            {
            }
            ClearSocket(socket);
        }
    }

    private async Task MaintenanceLoopAsync(
        OperatorRelayClientOptions options,
        CancellationToken cancellationToken)
    {
        var heartbeatInterval = options.HeartbeatInterval ?? TimeSpan.FromSeconds(5);
        var snapshotTimeout = options.SnapshotTimeout ?? TimeSpan.FromSeconds(3);
        var nextHeartbeat = DateTimeOffset.UtcNow + heartbeatInterval;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(250, cancellationToken);
            var now = DateTimeOffset.UtcNow;

            if (now >= nextHeartbeat)
            {
                try
                {
                    await SendMessageAsync(
                        LynxMessageCodec.Heartbeat(ClientId),
                        cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                nextHeartbeat = now + heartbeatInterval;
            }

            var timedOut = false;
            lock (_snapshotReceiptLock)
            {
                timedOut = _pendingSnapshotId is not null &&
                    _snapshotDeadlineUtc is not null &&
                    now >= _snapshotDeadlineUtc;
            }

            if (timedOut)
            {
                SetStatus($"Snapshot incomplete after {snapshotTimeout.TotalSeconds:0.#}s; requesting fresh snapshot.");
                await RequestSnapshotAsync(cancellationToken);
            }
        }
    }

    private void TrackSnapshotReceipt(
        LynxMessage message,
        OperatorRelayClientOptions options)
    {
        if (message.Type != LynxMessageTypes.Snapshot ||
            message.SnapshotId is null ||
            message.ChunkIndex is null ||
            message.ChunkCount is null)
            return;

        var snapshotTimeout = options.SnapshotTimeout ?? TimeSpan.FromSeconds(3);

        lock (_snapshotReceiptLock)
        {
            if (_pendingSnapshotId != message.SnapshotId)
            {
                _pendingSnapshotId = message.SnapshotId;
                _pendingSnapshotChunkCount = message.ChunkCount.Value;
                _receivedSnapshotChunks.Clear();
                _snapshotDeadlineUtc = DateTimeOffset.UtcNow + snapshotTimeout;
            }

            _receivedSnapshotChunks.Add(message.ChunkIndex.Value);
            if (_receivedSnapshotChunks.Count >= _pendingSnapshotChunkCount)
                ResetSnapshotReceiptLocked();
        }
    }

    private async Task SendMessageAsync(
        LynxMessage message,
        CancellationToken cancellationToken)
    {
        UdpClient socket;
        IPEndPoint endpoint;
        lock (_socketLock)
        {
            socket = _socket ?? throw new InvalidOperationException("Relay is not connected.");
            endpoint = _relayEndpoint ?? throw new InvalidOperationException("Relay endpoint is not available.");
        }

        var payload = LynxMessageCodec.Serialize(message);
        if (payload.Length > MaxPayloadBytes)
            throw new InvalidOperationException("UDP payload exceeds prototype limit.");

        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(payload, endpoint, cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void SetSocket(UdpClient socket, IPEndPoint endpoint)
    {
        lock (_socketLock)
        {
            _socket = socket;
            _relayEndpoint = endpoint;
        }

        RelayEndpointChanged?.Invoke(
            new RelayEndpoint(endpoint.Address.ToString(), endpoint.Port));
    }

    private void ClearSocket(UdpClient? expected = null)
    {
        lock (_socketLock)
        {
            if (expected is not null && !ReferenceEquals(_socket, expected))
                return;
            _socket = null;
            _relayEndpoint = null;
        }

        RelayEndpointChanged?.Invoke(null);
    }

    private void ResetSnapshotReceipt()
    {
        lock (_snapshotReceiptLock)
            ResetSnapshotReceiptLocked();
    }

    private void ResetSnapshotReceiptLocked()
    {
        _pendingSnapshotId = null;
        _pendingSnapshotChunkCount = 0;
        _receivedSnapshotChunks.Clear();
        _snapshotDeadlineUtc = null;
    }

    private void RaiseStateChanged() =>
        StateChanged?.Invoke(
            _state.Snapshot(),
            _state.AppliedEpoch,
            _state.AppliedVersion);

    private void SetStatus(string status) =>
        StatusChanged?.Invoke(status);

    private static long CreatePositiveRandomId() =>
        PositiveIdGenerator.Next();

    private static void ValidateOptions(OperatorRelayClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RelayHost))
            throw new ArgumentException("Relay host is required.", nameof(options));
        if (options.RelayPort is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public ValueTask DisposeAsync()
    {
        ClearSocket();
        _sendGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
