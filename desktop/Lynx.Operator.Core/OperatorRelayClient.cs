using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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
    private readonly AuthoritativeOverlayState _state = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _socketLock = new();
    private readonly object _snapshotReceiptLock = new();

    private UdpClient? _socket;
    private IPEndPoint? _relayEndpoint;
    private long? _pendingSnapshotId;
    private bool _initialOverlaySnapshotPending;
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
                    : new RelayEndpoint(_relayEndpoint.Address.ToString(), _relayEndpoint.Port);
            }
        }
    }

    public event Action<string>? StatusChanged;
    public event Action<RelayEndpoint?>? RelayEndpointChanged;
    public event Action<IReadOnlyList<OverlayMessage>, long, long>? StateChanged;

    public IReadOnlyList<OverlayMessage> StateSnapshot() => _state.Snapshot();

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
                    options.RelayHost, options.RelayPort, discoveryTimeout, cancellationToken);

                if (resolved is null)
                {
                    SetStatus("Relay not found; retrying.");
                    await Task.Delay(retryDelay, cancellationToken);
                    continue;
                }

                await RunConnectedSessionAsync(resolved, options, cancellationToken);
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

    public Task<OverlayCommandPreview> CreateMarkerAsync(
        SharedPoint shared,
        int beaconId = 0,
        CancellationToken cancellationToken = default) =>
        CreateOverlayAsync(
            OverlayKinds.Marker,
            new[] { new SharedPointMessage((float)shared.X, (float)shared.Y, (float)shared.Z) },
            beaconId,
            cancellationToken);

    public async Task<OverlayCommandPreview> CreateOverlayAsync(
        string kind,
        IReadOnlyList<SharedPointMessage> geometry,
        int beaconId = 0,
        CancellationToken cancellationToken = default)
    {
        if (!OverlayValidation.ValidateGeometry(kind, geometry, out var error))
            throw new ArgumentException(error, nameof(geometry));
        if (beaconId < 0)
            throw new ArgumentOutOfRangeException(nameof(beaconId));

        var id = PositiveIdGenerator.Next();
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await SendOverlayMessageAsync(
            OverlayWireCodec.CreateCommand(
                id, ClientId, beaconId, kind, geometry, createdAt),
            cancellationToken);

        return new OverlayCommandPreview(
            id, ClientId, beaconId, kind, geometry.ToArray(), createdAt);
    }

    public async Task UpdateOverlayAsync(
        long overlayId,
        IReadOnlyList<SharedPointMessage> geometry,
        CancellationToken cancellationToken = default)
    {
        if (!_state.TryGet(overlayId, out var overlay) || overlay is null)
            throw new InvalidOperationException($"Overlay {overlayId} is not in authoritative state.");
        if (overlay.OwnerId != ClientId)
            throw new InvalidOperationException("This operator does not own the overlay.");
        if (!OverlayValidation.ValidateGeometry(overlay.Kind, geometry, out var error))
            throw new ArgumentException(error, nameof(geometry));

        await SendOverlayMessageAsync(
            OverlayWireCodec.UpdateCommand(
                overlayId, ClientId, overlay.Revision, overlay.Kind, geometry),
            cancellationToken);
    }

    public async Task RemoveOverlayAsync(
        long overlayId,
        CancellationToken cancellationToken = default)
    {
        if (!_state.TryGet(overlayId, out var overlay) || overlay is null)
            throw new InvalidOperationException($"Overlay {overlayId} is not in authoritative state.");
        if (overlay.OwnerId != ClientId)
            throw new InvalidOperationException("This operator does not own the overlay.");

        await SendOverlayMessageAsync(
            OverlayWireCodec.RemoveCommand(overlayId, ClientId, overlay.Revision),
            cancellationToken);
    }

    public Task RemoveMarkerAsync(
        long overlayId,
        CancellationToken cancellationToken = default) =>
        RemoveOverlayAsync(overlayId, cancellationToken);

    public async Task RequestOverlaySnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        _state.ResetPendingSnapshot();
        ResetSnapshotReceipt();
        await SendOverlayMessageAsync(
            OverlayWireCodec.RequestSnapshot(ClientId),
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
        await SendLegacyMessageAsync(
            LynxMessageCodec.RegisterClient(ClientId), cancellationToken);
        _initialOverlaySnapshotPending = true;
        await RequestOverlaySnapshotAsync(cancellationToken);

        using var maintenanceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var maintenanceTask = MaintenanceLoopAsync(options, maintenanceCts.Token);

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

                var type = ReadMessageType(received.Buffer);
                if (type is null) continue;

                if (IsOverlayType(type))
                {
                    if (!OverlayWireCodec.TryParse(received.Buffer, out var message, out var error) || message is null)
                    {
                        SetStatus($"Dropped invalid overlay payload: {error}");
                        continue;
                    }
                    await HandleOverlayInboundAsync(message, options, cancellationToken);
                    continue;
                }

                // Legacy snapshots are retained only as restart-registration signals.
                if (type == LynxMessageTypes.Snapshot &&
                    !_initialOverlaySnapshotPending &&
                    LynxMessageCodec.TryParse(received.Buffer, out var legacy, out _) &&
                    legacy?.StateEpoch is long legacyEpoch &&
                    legacyEpoch > _state.AppliedEpoch)
                {
                    SetStatus("Relay epoch changed; requesting generalized overlay snapshot.");
                    await RequestOverlaySnapshotAsync(cancellationToken);
                }
            }
        }
        finally
        {
            maintenanceCts.Cancel();
            try { await maintenanceTask; } catch (OperationCanceledException) { }
            ClearSocket(socket);
        }
    }

    private async Task HandleOverlayInboundAsync(
        OverlayWireMessage message,
        OperatorRelayClientOptions options,
        CancellationToken cancellationToken)
    {
        TrackSnapshotReceipt(message, options);
        var result = _state.Apply(message);

        switch (result.Status)
        {
            case OverlayStateApplyStatus.ResyncRequired:
                SetStatus(
                    $"Overlay state gap/conflict at {_state.AppliedEpoch}:{_state.AppliedVersion}; resync.");
                await RequestOverlaySnapshotAsync(cancellationToken);
                break;

            case OverlayStateApplyStatus.SnapshotIncomplete:
                SetStatus($"Overlay snapshot chunk {message.ChunkIndex!.Value + 1}/{message.ChunkCount} received.");
                break;

            case OverlayStateApplyStatus.SnapshotApplied:
                _initialOverlaySnapshotPending = false;
                ResetSnapshotReceipt();
                SetStatus(
                    $"Overlay snapshot applied: {_state.Snapshot().Count} object(s), " +
                    $"epoch {_state.AppliedEpoch}, version {_state.AppliedVersion}.");
                RaiseStateChanged();
                break;

            case OverlayStateApplyStatus.Applied:
                SetStatus($"Overlay diff applied at {_state.AppliedEpoch}:{_state.AppliedVersion}.");
                if (result.StateChanged) RaiseStateChanged();
                break;

            case OverlayStateApplyStatus.StaleIgnored:
                SetStatus("Stale/duplicate overlay packet ignored.");
                break;
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
                    await SendLegacyMessageAsync(
                        LynxMessageCodec.Heartbeat(ClientId), cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                nextHeartbeat = now + heartbeatInterval;
            }

            bool timedOut;
            lock (_snapshotReceiptLock)
            {
                timedOut = _pendingSnapshotId is not null &&
                    _snapshotDeadlineUtc is not null && now >= _snapshotDeadlineUtc;
            }

            if (timedOut)
            {
                SetStatus($"Overlay snapshot incomplete after {snapshotTimeout.TotalSeconds:0.#}s; resync.");
                await RequestOverlaySnapshotAsync(cancellationToken);
            }
        }
    }

    private void TrackSnapshotReceipt(
        OverlayWireMessage message,
        OperatorRelayClientOptions options)
    {
        if (message.Type != OverlayMessageTypes.Snapshot ||
            message.SnapshotId is null ||
            message.ChunkIndex is null ||
            message.ChunkCount is null)
            return;

        var timeout = options.SnapshotTimeout ?? TimeSpan.FromSeconds(3);
        lock (_snapshotReceiptLock)
        {
            if (_pendingSnapshotId != message.SnapshotId)
            {
                _pendingSnapshotId = message.SnapshotId;
                _pendingSnapshotChunkCount = message.ChunkCount.Value;
                _receivedSnapshotChunks.Clear();
                _snapshotDeadlineUtc = DateTimeOffset.UtcNow + timeout;
            }

            _receivedSnapshotChunks.Add(message.ChunkIndex.Value);
            if (_receivedSnapshotChunks.Count >= _pendingSnapshotChunkCount)
                ResetSnapshotReceiptLocked();
        }
    }

    private async Task SendLegacyMessageAsync(
        LynxMessage message,
        CancellationToken cancellationToken) =>
        await SendBytesAsync(LynxMessageCodec.Serialize(message), cancellationToken);

    private async Task SendOverlayMessageAsync(
        OverlayWireMessage message,
        CancellationToken cancellationToken) =>
        await SendBytesAsync(OverlayWireCodec.Serialize(message), cancellationToken);

    private async Task SendBytesAsync(byte[] payload, CancellationToken cancellationToken)
    {
        UdpClient socket;
        IPEndPoint endpoint;
        lock (_socketLock)
        {
            socket = _socket ?? throw new InvalidOperationException("Relay is not connected.");
            endpoint = _relayEndpoint ?? throw new InvalidOperationException("Relay endpoint unavailable.");
        }

        if (payload.Length > MaxPayloadBytes)
            throw new InvalidOperationException("UDP payload exceeds prototype limit.");

        await _sendGate.WaitAsync(cancellationToken);
        try { await socket.SendAsync(payload, endpoint, cancellationToken); }
        finally { _sendGate.Release(); }
    }

    private void SetSocket(UdpClient socket, IPEndPoint endpoint)
    {
        lock (_socketLock)
        {
            _socket = socket;
            _relayEndpoint = endpoint;
        }
        RelayEndpointChanged?.Invoke(new RelayEndpoint(endpoint.Address.ToString(), endpoint.Port));
    }

    private void ClearSocket(UdpClient? expected = null)
    {
        lock (_socketLock)
        {
            if (expected is not null && !ReferenceEquals(_socket, expected)) return;
            _socket = null;
            _relayEndpoint = null;
        }
        RelayEndpointChanged?.Invoke(null);
    }

    private void ResetSnapshotReceipt()
    {
        lock (_snapshotReceiptLock) ResetSnapshotReceiptLocked();
    }

    private void ResetSnapshotReceiptLocked()
    {
        _pendingSnapshotId = null;
        _pendingSnapshotChunkCount = 0;
        _receivedSnapshotChunks.Clear();
        _snapshotDeadlineUtc = null;
    }

    private void RaiseStateChanged() =>
        StateChanged?.Invoke(_state.Snapshot(), _state.AppliedEpoch, _state.AppliedVersion);

    private void SetStatus(string status) => StatusChanged?.Invoke(status);

    private static string? ReadMessageType(byte[] payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsOverlayType(string type) =>
        type is OverlayMessageTypes.Diff or OverlayMessageTypes.Snapshot;

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
