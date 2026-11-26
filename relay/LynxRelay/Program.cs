using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Lynx.Protocol;
using Lynx.Relay.Core;

const int MaxPayloadBytes = 4096;
const int SnapshotPayloadBudgetBytes = 1200;
var listenPort = 7777;
IPEndPoint? legacyForwardTo = null;
var clientTimeout = TimeSpan.FromSeconds(15);

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--listen" when i + 1 < args.Length:
            listenPort = int.Parse(args[++i]);
            break;
        case "--forward" when i + 1 < args.Length:
            legacyForwardTo = ParseEndpoint(args[++i]);
            break;
        case "--client-timeout-seconds" when i + 1 < args.Length:
            clientTimeout = TimeSpan.FromSeconds(int.Parse(args[++i]));
            break;
    }
}

var overlays = new OverlayStore();
var clients = new ClientRegistry();
var stateEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
long stateVersion = 0;
long nextLegacySnapshotId = 1;
long nextOverlaySnapshotId = 1;

using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));

Console.WriteLine($"[Relay] Listening on UDP :{listenPort}");
Console.WriteLine($"[Relay] Authoritative model: generalized overlays");
Console.WriteLine($"[Relay] Client timeout: {clientTimeout.TotalSeconds:F0}s");
if (legacyForwardTo is not null)
    Console.WriteLine($"[Relay] Legacy forward: {legacyForwardTo}");
Console.WriteLine($"[Relay] Max payload: {MaxPayloadBytes} bytes");
Console.WriteLine($"[Relay] Snapshot payload budget: {SnapshotPayloadBudgetBytes} bytes");
Console.WriteLine($"[Relay] State epoch: {stateEpoch}");
Console.WriteLine($"[Relay] Discovery shares UDP :{listenPort}");
Console.WriteLine("[Relay] Ctrl+C to stop.");
Console.WriteLine();

while (true)
{
    UdpReceiveResult received;
    try
    {
        received = await udp.ReceiveAsync();
    }
    catch (SocketException ex)
    {
        Console.WriteLine($"[Relay] Transient UDP receive error {ex.SocketErrorCode}; continuing.");
        await Task.Delay(100);
        continue;
    }

    var payload = received.Buffer;
    var from = received.RemoteEndPoint;
    var now = DateTimeOffset.UtcNow;

    if (payload.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(RelayDiscoveryProtocol.Request)))
    {
        var response = Encoding.UTF8.GetBytes(RelayDiscoveryProtocol.CreateResponse(listenPort));
        await udp.SendAsync(response, from);
        Console.WriteLine($"[Relay] DISCOVERY reply to {from} relayPort={listenPort}");
        continue;
    }

    var expired = clients.RemoveExpired(now, clientTimeout);
    if (expired > 0)
        Console.WriteLine($"[Relay] Expired {expired} client(s); active={clients.Count}");

    if (payload.Length > MaxPayloadBytes)
    {
        Console.WriteLine($"[Relay] DROP from {from}: payload too large ({payload.Length} > {MaxPayloadBytes})");
        continue;
    }

    var messageType = ReadMessageType(payload);
    if (messageType is null)
    {
        Console.WriteLine($"[Relay] DROP from {from}: missing/invalid message type");
        continue;
    }

    if (IsOverlayMessageType(messageType))
    {
        if (!OverlayWireCodec.TryParse(payload, out var overlayMessage, out var overlayError) || overlayMessage is null)
        {
            Console.WriteLine($"[Relay] DROP overlay from {from}: {overlayError}");
            continue;
        }

        await HandleOverlayMessage(overlayMessage, from, now);
        continue;
    }

    if (!LynxMessageCodec.TryParse(payload, out var legacyMessage, out var legacyError) || legacyMessage is null)
    {
        Console.WriteLine($"[Relay] DROP from {from}: {legacyError}");
        continue;
    }

    await HandleLegacyMessage(legacyMessage, from, now);
}

async Task HandleOverlayMessage(
    OverlayWireMessage message,
    IPEndPoint from,
    DateTimeOffset now)
{
    switch (message.Type)
    {
        case OverlayMessageTypes.SnapshotRequest:
        {
            var clientId = message.ClientId!.Value;
            clients.RegisterOrRefresh(clientId, from, now);
            await SendOverlaySnapshot(from, clientId, "OVERLAY-SNAPSHOT-REQUEST");
            break;
        }

        case OverlayMessageTypes.CreateCommand:
        {
            var senderId = message.SenderId!.Value;
            if (!RegisteredSenderMatches(senderId, from))
            {
                Console.WriteLine($"[Relay] OVERLAY_CREATE rejected id={message.OverlayId} from {from}: sender not registered at endpoint");
                break;
            }

            var outcome = overlays.Create(
                message.OverlayId!.Value,
                senderId,
                message.BeaconId!.Value,
                message.Kind!,
                message.Geometry!,
                message.CreatedAt!.Value);

            if (outcome.Result == OverlayMutationResult.Duplicate)
            {
                Console.WriteLine($"[Relay] DUP overlay create ignored id={message.OverlayId} from {from}");
                break;
            }
            if (outcome.Result != OverlayMutationResult.Created)
            {
                Console.WriteLine($"[Relay] CONFLICT overlay create rejected id={message.OverlayId} from {from}");
                break;
            }

            stateVersion++;
            var overlay = outcome.Overlay!;
            var diffPayload = OverlayWireCodec.Serialize(
                OverlayWireCodec.UpsertDiff(overlay, stateEpoch, stateVersion));

            Console.WriteLine(
                $"[Relay] OVERLAY CREATE id={overlay.OverlayId} kind={overlay.Kind} owner={overlay.OwnerId} " +
                $"rev={overlay.Revision} stored={overlays.Count} version={stateVersion}");
            await BroadcastAccepted(diffPayload, $"OVERLAY_DIFF UPSERT id={overlay.OverlayId} rev={overlay.Revision} version={stateVersion}");

            if (overlay.Kind == OverlayKinds.Marker)
                await BroadcastLegacyMarkerCreate(overlay);
            break;
        }

        case OverlayMessageTypes.UpdateCommand:
        {
            var senderId = message.SenderId!.Value;
            if (!RegisteredSenderMatches(senderId, from))
            {
                Console.WriteLine($"[Relay] OVERLAY_UPDATE rejected id={message.OverlayId} from {from}: sender not registered at endpoint");
                break;
            }

            var outcome = overlays.Update(
                message.OverlayId!.Value,
                senderId,
                message.ExpectedRevision!.Value,
                message.Kind!,
                message.Geometry!,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            if (outcome.Result != OverlayMutationResult.Updated)
            {
                Console.WriteLine($"[Relay] OVERLAY_UPDATE rejected id={message.OverlayId} result={outcome.Result}");
                break;
            }

            stateVersion++;
            var overlay = outcome.Overlay!;
            var diffPayload = OverlayWireCodec.Serialize(
                OverlayWireCodec.UpsertDiff(overlay, stateEpoch, stateVersion));
            Console.WriteLine(
                $"[Relay] OVERLAY UPDATE id={overlay.OverlayId} kind={overlay.Kind} rev={overlay.Revision} version={stateVersion}");
            await BroadcastAccepted(diffPayload, $"OVERLAY_DIFF UPSERT id={overlay.OverlayId} rev={overlay.Revision} version={stateVersion}");
            break;
        }

        case OverlayMessageTypes.RemoveCommand:
        {
            var senderId = message.SenderId!.Value;
            if (!RegisteredSenderMatches(senderId, from))
            {
                Console.WriteLine($"[Relay] OVERLAY_REMOVE rejected id={message.OverlayId} from {from}: sender not registered at endpoint");
                break;
            }

            overlays.TryGet(message.OverlayId!.Value, out var existing);
            var outcome = overlays.Remove(
                message.OverlayId.Value,
                senderId,
                message.ExpectedRevision!.Value);

            if (outcome.Result != OverlayMutationResult.Removed)
            {
                Console.WriteLine($"[Relay] OVERLAY_REMOVE rejected id={message.OverlayId} result={outcome.Result}");
                break;
            }

            stateVersion++;
            var removedRevision = outcome.RemovedRevision!.Value;
            var diffPayload = OverlayWireCodec.Serialize(
                OverlayWireCodec.RemoveDiff(message.OverlayId.Value, removedRevision, stateEpoch, stateVersion));
            Console.WriteLine(
                $"[Relay] OVERLAY REMOVE id={message.OverlayId} rev={removedRevision} version={stateVersion}");
            await BroadcastAccepted(diffPayload, $"OVERLAY_DIFF REMOVE id={message.OverlayId} rev={removedRevision} version={stateVersion}");

            if (existing?.Kind == OverlayKinds.Marker)
                await BroadcastLegacyMarkerRemove(existing.OverlayId, existing.OwnerId);
            break;
        }

        case OverlayMessageTypes.Diff:
        case OverlayMessageTypes.Snapshot:
            Console.WriteLine($"[Relay] DROP from {from}: clients may not send {message.Type}");
            break;
    }
}

async Task HandleLegacyMessage(
    LynxMessage message,
    IPEndPoint from,
    DateTimeOffset now)
{
    switch (message.Type)
    {
        case LynxMessageTypes.ClientRegister:
        {
            var clientId = message.ClientId!.Value;
            clients.RegisterOrRefresh(clientId, from, now);
            await SendLegacySnapshot(from, clientId, "REGISTER");
            break;
        }

        case LynxMessageTypes.ClientHeartbeat:
        {
            var clientId = message.ClientId!.Value;
            var knownClient = clients.TryGet(clientId, out _);
            clients.RegisterOrRefresh(clientId, from, now);
            if (!knownClient)
                await SendLegacySnapshot(from, clientId, "HEARTBEAT-REGISTER");
            else
                Console.WriteLine($"[Relay] HEARTBEAT client={clientId} endpoint={from} active={clients.Count}");
            break;
        }

        case LynxMessageTypes.SnapshotRequest:
        {
            var clientId = message.ClientId!.Value;
            clients.RegisterOrRefresh(clientId, from, now);
            await SendLegacySnapshot(from, clientId, "SNAPSHOT-REQUEST");
            break;
        }

        case LynxMessageTypes.PingCreate:
        {
            var ping = message.Ping!;
            var outcome = overlays.Create(
                ping.Id, ping.SenderId, ping.BeaconId, OverlayKinds.Marker,
                new[] { new SharedPointMessage(ping.X, ping.Y, ping.Z) },
                ping.CreatedAt);

            if (outcome.Result == OverlayMutationResult.Duplicate)
            {
                Console.WriteLine($"[Relay] DUP create ignored id={ping.Id} from {from}");
                break;
            }
            if (outcome.Result != OverlayMutationResult.Created)
            {
                Console.WriteLine($"[Relay] CONFLICT rejected id={ping.Id} from {from}: same id already exists with different contents");
                break;
            }

            stateVersion++;
            var overlay = outcome.Overlay!;
            var legacyPayload = LynxMessageCodec.Serialize(
                LynxMessageCodec.CreatePing(ping, stateEpoch, stateVersion));
            var diffPayload = OverlayWireCodec.Serialize(
                OverlayWireCodec.UpsertDiff(overlay, stateEpoch, stateVersion));

            Console.WriteLine(
                $"[Relay] CREATE id={ping.Id} sender={ping.SenderId} beacon={ping.BeaconId} " +
                $"shared=({ping.X:F2}, {ping.Y:F2}, {ping.Z:F2}) from {from} stored={overlays.Count} version={stateVersion}");
            await BroadcastAccepted(legacyPayload, $"PING_CREATE id={ping.Id} version={stateVersion}");
            await BroadcastAccepted(diffPayload, $"OVERLAY_DIFF UPSERT id={ping.Id} rev=1 version={stateVersion}");
            break;
        }

        case LynxMessageTypes.PingRemove:
        {
            var pingId = message.PingId!.Value;
            var senderId = message.SenderId!.Value;
            if (!overlays.TryGet(pingId, out var existing) || existing is null)
            {
                Console.WriteLine($"[Relay] DUP remove ignored id={pingId} from {from}");
                break;
            }
            if (existing.Kind != OverlayKinds.Marker)
            {
                Console.WriteLine($"[Relay] REMOVE rejected id={pingId}: overlay is not marker");
                break;
            }

            var outcome = overlays.Remove(pingId, senderId, existing.Revision);
            if (outcome.Result == OverlayMutationResult.OwnerMismatch)
            {
                Console.WriteLine($"[Relay] REMOVE rejected id={pingId} from {from}: sender {senderId} does not own ping");
                break;
            }
            if (outcome.Result != OverlayMutationResult.Removed)
            {
                Console.WriteLine($"[Relay] REMOVE rejected id={pingId} result={outcome.Result}");
                break;
            }

            stateVersion++;
            var legacyPayload = LynxMessageCodec.Serialize(
                LynxMessageCodec.RemovePing(pingId, senderId, stateEpoch, stateVersion));
            var diffPayload = OverlayWireCodec.Serialize(
                OverlayWireCodec.RemoveDiff(pingId, outcome.RemovedRevision!.Value, stateEpoch, stateVersion));

            Console.WriteLine($"[Relay] REMOVE id={pingId} sender={senderId} from {from} stored={overlays.Count} version={stateVersion}");
            await BroadcastAccepted(legacyPayload, $"PING_REMOVE id={pingId} version={stateVersion}");
            await BroadcastAccepted(diffPayload, $"OVERLAY_DIFF REMOVE id={pingId} rev={outcome.RemovedRevision} version={stateVersion}");
            break;
        }

        case LynxMessageTypes.Snapshot:
            Console.WriteLine($"[Relay] DROP from {from}: clients may not send SNAPSHOT");
            break;
    }
}

async Task SendLegacySnapshot(IPEndPoint endpoint, long clientId, string reason)
{
    var snapshotPings = overlays.Snapshot()
        .Where(overlay => overlay.Kind == OverlayKinds.Marker)
        .Select(ToLegacyPing)
        .ToArray();
    var snapshotId = nextLegacySnapshotId++;
    var chunks = SnapshotChunker.ChunkBySerializedBudget(
        snapshotPings, snapshotId, stateEpoch, stateVersion, SnapshotPayloadBudgetBytes);

    for (var i = 0; i < chunks.Count; i++)
    {
        var bytes = LynxMessageCodec.Serialize(
            LynxMessageCodec.SnapshotChunk(snapshotId, stateEpoch, stateVersion, i, chunks.Count, chunks[i]));
        Console.WriteLine(
            $"[Relay] SNAPSHOT chunk={i + 1}/{chunks.Count} bytes={bytes.Length} snapshotId={snapshotId}");
        await udp.SendAsync(bytes, endpoint);
    }

    Console.WriteLine(
        $"[Relay] {reason} client={clientId} endpoint={endpoint} active={clients.Count} " +
        $"snapshot={snapshotPings.Length} snapshotId={snapshotId} chunks={chunks.Count} epoch={stateEpoch} version={stateVersion}");
}

async Task SendOverlaySnapshot(IPEndPoint endpoint, long clientId, string reason)
{
    var snapshot = overlays.Snapshot();
    var snapshotId = nextOverlaySnapshotId++;
    var chunks = OverlaySnapshotChunker.ChunkBySerializedBudget(
        snapshot, snapshotId, stateEpoch, stateVersion, SnapshotPayloadBudgetBytes);

    for (var i = 0; i < chunks.Count; i++)
    {
        var bytes = OverlayWireCodec.Serialize(
            OverlayWireCodec.SnapshotChunk(snapshotId, stateEpoch, stateVersion, i, chunks.Count, chunks[i]));
        if (bytes.Length > SnapshotPayloadBudgetBytes)
            throw new InvalidOperationException("Overlay snapshot chunk exceeded payload budget.");
        await udp.SendAsync(bytes, endpoint);
        Console.WriteLine($"[Relay] OVERLAY_SNAPSHOT chunk={i + 1}/{chunks.Count} bytes={bytes.Length} snapshotId={snapshotId}");
    }

    Console.WriteLine(
        $"[Relay] {reason} client={clientId} overlaySnapshot={snapshot.Count} snapshotId={snapshotId} " +
        $"chunks={chunks.Count} epoch={stateEpoch} version={stateVersion}");
}

async Task BroadcastLegacyMarkerCreate(OverlayMessage overlay)
{
    var ping = ToLegacyPing(overlay);
    var bytes = LynxMessageCodec.Serialize(
        LynxMessageCodec.CreatePing(ping, stateEpoch, stateVersion));
    await BroadcastAccepted(bytes, $"PING_CREATE id={ping.Id} version={stateVersion}");
}

async Task BroadcastLegacyMarkerRemove(long overlayId, long ownerId)
{
    var bytes = LynxMessageCodec.Serialize(
        LynxMessageCodec.RemovePing(overlayId, ownerId, stateEpoch, stateVersion));
    await BroadcastAccepted(bytes, $"PING_REMOVE id={overlayId} version={stateVersion}");
}

async Task BroadcastAccepted(byte[] payload, string description)
{
    var endpoints = clients.Clients.Select(client => client.Endpoint).ToList();
    if (legacyForwardTo is not null && endpoints.All(endpoint => !endpoint.Equals(legacyForwardTo)))
        endpoints.Add(legacyForwardTo);
    foreach (var endpoint in endpoints)
        await udp.SendAsync(payload, endpoint);
    Console.WriteLine($"[Relay] -> broadcast {description} to {endpoints.Count} endpoint(s)");
}

bool RegisteredSenderMatches(long senderId, IPEndPoint from) =>
    clients.TryGet(senderId, out var client) && client is not null && client.Endpoint.Equals(from);

static PingMessage ToLegacyPing(OverlayMessage overlay)
{
    var point = overlay.Geometry[0];
    return new PingMessage(
        overlay.OverlayId, overlay.OwnerId, overlay.BeaconId,
        point.X, point.Y, point.Z, overlay.CreatedAt);
}

static string? ReadMessageType(byte[] payload)
{
    try
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.TryGetProperty("type", out var type)
            ? type.GetString()
            : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

static bool IsOverlayMessageType(string type) =>
    type is OverlayMessageTypes.CreateCommand or
        OverlayMessageTypes.UpdateCommand or
        OverlayMessageTypes.RemoveCommand or
        OverlayMessageTypes.Diff or
        OverlayMessageTypes.SnapshotRequest or
        OverlayMessageTypes.Snapshot;

static IPEndPoint ParseEndpoint(string value)
{
    var split = value.LastIndexOf(':');
    if (split <= 0 || split == value.Length - 1)
        throw new ArgumentException($"Bad endpoint '{value}'. Expected ip:port.");
    return new IPEndPoint(IPAddress.Parse(value[..split]), int.Parse(value[(split + 1)..]));
}
