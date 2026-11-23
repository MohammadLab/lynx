using System.Net;
using System.Net.Sockets;
using Lynx.Protocol;
using Lynx.Relay.Core;

const int MaxPayloadBytes = 4096;

var listenPort = 7777;
var forwardTo = new IPEndPoint(IPAddress.Loopback, 7778);

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--listen" when i + 1 < args.Length:
            listenPort = int.Parse(args[++i]);
            break;

        case "--forward" when i + 1 < args.Length:
            forwardTo = ParseEndpoint(args[++i]);
            break;
    }
}

var pings = new PingStore();

using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));

Console.WriteLine($"[Relay] Listening on UDP :{listenPort}");
Console.WriteLine($"[Relay] Forwarding accepted messages to {forwardTo}");
Console.WriteLine($"[Relay] Max payload: {MaxPayloadBytes} bytes");
Console.WriteLine("[Relay] Ctrl+C to stop.");
Console.WriteLine();

while (true)
{
    var received = await udp.ReceiveAsync();
    var payload = received.Buffer;
    var from = received.RemoteEndPoint;

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
        case LynxMessageTypes.PingCreate:
        {
            var ping = message.Ping!;

            switch (pings.TryAdd(ping))
            {
                case PingAddResult.Duplicate:
                    Console.WriteLine(
                        $"[Relay] DUP create ignored id={ping.Id} from {from}");
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

            await udp.SendAsync(payload, forwardTo);

            Console.WriteLine(
                $"[Relay] -> forwarded PING_CREATE id={ping.Id} to {forwardTo}");
            break;
        }

        case LynxMessageTypes.PingRemove:
        {
            var pingId = message.PingId!.Value;
            var senderId = message.SenderId!.Value;

            switch (pings.TryRemove(pingId, senderId))
            {
                case PingRemoveResult.NotFound:
                    Console.WriteLine(
                        $"[Relay] DUP remove ignored id={pingId} from {from}");
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

            await udp.SendAsync(payload, forwardTo);

            Console.WriteLine(
                $"[Relay] -> forwarded PING_REMOVE id={pingId} to {forwardTo}");
            break;
        }
    }
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
