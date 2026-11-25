using System.Net;
using System.Net.Sockets;
using Lynx.Protocol;
using Lynx.Relay.Core;

const int MaxPayloadBytes = 4096;
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

using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));

Console.WriteLine($"[Relay] Listening on UDP :{listenPort}");
Console.WriteLine($"[Relay] Client timeout: {clientTimeout.TotalSeconds:F0}s");
if (legacyForwardTo is not null)
    Console.WriteLine($"[Relay] Legacy forward: {legacyForwardTo}");
Console.WriteLine($"[Relay] Max payload: {MaxPayloadBytes} bytes");
Console.WriteLine("[Relay] Ctrl+C to stop.");
Console.WriteLine();

while (true)
{
    var received = await udp.ReceiveAsync();
    var payload = received.Buffer;
    var from = received.RemoteEndPoint;
    var now = DateTimeOffset.UtcNow;

    var expired = clients.RemoveExpired(now, clientTimeout);
    if (expired > 0)
        Console.WriteLine($"[Relay] Expired {expired} client(s); active={clients.Count}");

    if (payload.Length > MaxPayloadBytes)
    {
        Console.WriteLine(
            $"[Relay] DROP from {from}: payload too large " +
            $"({payload.Length} > {MaxPayloadBytes})");
        continue;
    }

    if (!LynxMessageCodec.TryParse(payload, out var message, out var error) ||
        message is null)
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

            var snapshot = LynxMessageCodec.Serialize(
                LynxMessageCodec.Snapshot(pings.Snapshot()));
            await udp.SendAsync(snapshot, from);

            Console.WriteLine(
                $"[Relay] REGISTER client={clientId} endpoint={from} " +
                $"active={clients.Count} snapshot={pings.Count}");
            break;
        }

        case LynxMessageTypes.ClientHeartbeat:
        {
            var clientId = message.ClientId!.Value;
            clients.RegisterOrRefresh(clientId, from, now);
            Console.WriteLine(
                $"[Relay] HEARTBEAT client={clientId} endpoint={from} active={clients.Count}");
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
                    Console.WriteLine(
                        $"[Relay] CONFLICT rejected id={ping.Id} from {from}: " +
                        "same id already exists with different contents");
                    continue;

                case PingAddResult.Added:
                    break;
            }

            Console.WriteLine(
                $"[Relay] CREATE id={ping.Id} sender={ping.SenderId} " +
                $"beacon={ping.BeaconId} " +
                $"shared=({ping.X:F2}, {ping.Y:F2}, {ping.Z:F2}) from {from} " +
                $"stored={pings.Count}");

            await BroadcastAccepted(payload, $"PING_CREATE id={ping.Id}");
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
                    Console.WriteLine(
                        $"[Relay] REMOVE rejected id={pingId} from {from}: " +
                        $"sender {senderId} does not own ping");
                    continue;

                case PingRemoveResult.Removed:
                    break;
            }

            Console.WriteLine(
                $"[Relay] REMOVE id={pingId} sender={senderId} from {from} " +
                $"stored={pings.Count}");

            await BroadcastAccepted(payload, $"PING_REMOVE id={pingId}");
            break;
        }

        case LynxMessageTypes.Snapshot:
            Console.WriteLine($"[Relay] DROP from {from}: clients may not send SNAPSHOT");
            break;
    }
}

async Task BroadcastAccepted(byte[] payload, string description)
{
    var endpoints = clients.Clients
        .Select(client => client.Endpoint)
        .ToList();

    if (legacyForwardTo is not null &&
        endpoints.All(endpoint => !endpoint.Equals(legacyForwardTo)))
    {
        endpoints.Add(legacyForwardTo);
    }

    foreach (var endpoint in endpoints)
    {
        await udp.SendAsync(payload, endpoint);
    }

    Console.WriteLine(
        $"[Relay] -> broadcast {description} to {endpoints.Count} endpoint(s)");
}

static IPEndPoint ParseEndpoint(string value)
{
    var split = value.LastIndexOf(':');
    if (split <= 0 || split == value.Length - 1)
        throw new ArgumentException($"Bad endpoint '{value}'. Expected ip:port.");

    var ip = IPAddress.Parse(value[..split]);
    var port = int.Parse(value[(split + 1)..]);
    return new IPEndPoint(ip, port);
}