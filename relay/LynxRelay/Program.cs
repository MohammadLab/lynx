using System.Net;
using System.Net.Sockets;
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

var pings = new PingStore();
var clients = new ClientRegistry();
var stateEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
long stateVersion = 0;
long nextSnapshotId = 1;

using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));

Console.WriteLine($"[Relay] Listening on UDP :{listenPort}");
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
    var received = await udp.ReceiveAsync();
    var payload = received.Buffer;
    var from = received.RemoteEndPoint;
    var now = DateTimeOffset.UtcNow;

    var discoveryRequest = System.Text.Encoding.UTF8.GetBytes(
        RelayDiscoveryProtocol.Request);
    if (payload.AsSpan().SequenceEqual(discoveryRequest))
    {
        var response = System.Text.Encoding.UTF8.GetBytes(
            RelayDiscoveryProtocol.CreateResponse(listenPort));
        await udp.SendAsync(response, from);
        Console.WriteLine(
            $"[Relay] DISCOVERY reply to {from} relayPort={listenPort}");
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

    if (!LynxMessageCodec.TryParse(payload, out var message, out var error) || message is null)
    {
        Console.WriteLine($"[Relay] DROP from {from}: {error}");
        continue;
    }

    switch (message.Type)
    {
        case LynxMessageTypes.ClientRegister:
        {
            var clientId = message.ClientId!.Value;
            clients.RegisterOrRefresh(clientId, from, now);
            await SendSnapshot(from, clientId, "REGISTER");
            break;
        }

        case LynxMessageTypes.ClientHeartbeat:
        {
            var clientId = message.ClientId!.Value;
            var knownClient = clients.TryGet(clientId, out _);
            clients.RegisterOrRefresh(clientId, from, now);

            if (!knownClient)
            {
                await SendSnapshot(from, clientId, "HEARTBEAT-REGISTER");
            }
            else
            {
                Console.WriteLine($"[Relay] HEARTBEAT client={clientId} endpoint={from} active={clients.Count}");
            }
            break;
        }

        case LynxMessageTypes.SnapshotRequest:
        {
            var clientId = message.ClientId!.Value;
            clients.RegisterOrRefresh(clientId, from, now);
            await SendSnapshot(from, clientId, "SNAPSHOT-REQUEST");
            break;
        }

        case LynxMessageTypes.PingCreate:
        {
            var ping = message.Ping!;
            switch (pings.TryAdd(ping))
            {
                case PingAddResult.Duplicate:
                    Console.WriteLine($"[Relay] DUP create ignored id={ping.Id} from {from}");
                    continue;
                case PingAddResult.Conflict:
                    Console.WriteLine($"[Relay] CONFLICT rejected id={ping.Id} from {from}: same id already exists with different contents");
                    continue;
                case PingAddResult.Added:
                    break;
            }

            stateVersion++;
            var authoritativePayload = LynxMessageCodec.Serialize(
                LynxMessageCodec.CreatePing(ping, stateEpoch, stateVersion));

            Console.WriteLine(
                $"[Relay] CREATE id={ping.Id} sender={ping.SenderId} beacon={ping.BeaconId} " +
                $"shared=({ping.X:F2}, {ping.Y:F2}, {ping.Z:F2}) from {from} stored={pings.Count} version={stateVersion}");

            await BroadcastAccepted(authoritativePayload, $"PING_CREATE id={ping.Id} version={stateVersion}");
            break;
        }

        case LynxMessageTypes.PingRemove:
        {
            var pingId = message.PingId!.Value;
            var senderId = message.SenderId!.Value;
            switch (pings.TryRemove(pingId, senderId))
            {
                case PingRemoveResult.NotFound:
                    Console.WriteLine($"[Relay] DUP remove ignored id={pingId} from {from}");
                    continue;
                case PingRemoveResult.SenderMismatch:
                    Console.WriteLine($"[Relay] REMOVE rejected id={pingId} from {from}: sender {senderId} does not own ping");
                    continue;
                case PingRemoveResult.Removed:
                    break;
            }

            stateVersion++;
            var authoritativePayload = LynxMessageCodec.Serialize(
                LynxMessageCodec.RemovePing(pingId, senderId, stateEpoch, stateVersion));

            Console.WriteLine($"[Relay] REMOVE id={pingId} sender={senderId} from {from} stored={pings.Count} version={stateVersion}");
            await BroadcastAccepted(authoritativePayload, $"PING_REMOVE id={pingId} version={stateVersion}");
            break;
        }

        case LynxMessageTypes.Snapshot:
            Console.WriteLine($"[Relay] DROP from {from}: clients may not send SNAPSHOT");
            break;
    }
}


async Task SendSnapshot(IPEndPoint endpoint, long clientId, string reason)
{
    var snapshotPings = pings.Snapshot();
    var snapshotId = nextSnapshotId++;
    var chunks = SnapshotChunker.ChunkBySerializedBudget(
        snapshotPings,
        snapshotId,
        stateEpoch,
        stateVersion,
        SnapshotPayloadBudgetBytes);
    var chunkCount = chunks.Count;

    for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
    {
        var snapshotPayload = LynxMessageCodec.Serialize(
            LynxMessageCodec.SnapshotChunk(
                snapshotId,
                stateEpoch,
                stateVersion,
                chunkIndex,
                chunkCount,
                chunks[chunkIndex]));

        if (snapshotPayload.Length > SnapshotPayloadBudgetBytes)
            throw new InvalidOperationException("Snapshot chunk exceeded payload budget.");

        Console.WriteLine(
            $"[Relay] SNAPSHOT chunk={chunkIndex + 1}/{chunkCount} " +
            $"bytes={snapshotPayload.Length} snapshotId={snapshotId}");

        await udp.SendAsync(snapshotPayload, endpoint);
    }

    Console.WriteLine(
        $"[Relay] {reason} client={clientId} endpoint={endpoint} active={clients.Count} " +
        $"snapshot={snapshotPings.Count} snapshotId={snapshotId} chunks={chunkCount} " +
        $"epoch={stateEpoch} version={stateVersion}");
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

static IPEndPoint ParseEndpoint(string value)
{
    var split = value.LastIndexOf(':');
    if (split <= 0 || split == value.Length - 1)
        throw new ArgumentException($"Bad endpoint '{value}'. Expected ip:port.");
    return new IPEndPoint(IPAddress.Parse(value[..split]), int.Parse(value[(split + 1)..]));
}