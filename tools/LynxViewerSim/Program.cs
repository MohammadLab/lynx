using System.Net;
using System.Net.Sockets;
using Lynx.Protocol;

var listenPort = 7778;
var expectedBeaconId = 0;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--listen" when i + 1 < args.Length:
            listenPort = int.Parse(args[++i]);
            break;

        case "--beacon" when i + 1 < args.Length:
            expectedBeaconId = int.Parse(args[++i]);
            break;
    }
}

var receivedPings = new Dictionary<long, PingMessage>();

using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));

Console.WriteLine($"[ViewerSim] Listening on UDP :{listenPort}");
Console.WriteLine($"[ViewerSim] Shared frame beaconId={expectedBeaconId}");
Console.WriteLine("[ViewerSim] Acting as Device B.");
Console.WriteLine("[ViewerSim] Ctrl+C to stop.");
Console.WriteLine();

while (true)
{
    var received = await udp.ReceiveAsync();
    var payload = received.Buffer;

    if (!LynxMessageCodec.TryParse(payload, out var message, out var error) ||
        message is null)
    {
        Console.WriteLine(
            $"[ViewerSim] DROP from {received.RemoteEndPoint}: {error}");
        continue;
    }

    switch (message.Type)
    {
        case LynxMessageTypes.PingCreate:
        {
            var ping = message.Ping!;

            if (ping.BeaconId != expectedBeaconId)
            {
                Console.WriteLine(
                    $"[ViewerSim] FRAME MISMATCH ignored id={ping.Id}: " +
                    $"ping beacon={ping.BeaconId}, local beacon={expectedBeaconId}");
                continue;
            }

            if (receivedPings.TryGetValue(ping.Id, out var existing) &&
                existing == ping)
            {
                Console.WriteLine($"[ViewerSim] DUP create ignored id={ping.Id}");
                continue;
            }

            receivedPings[ping.Id] = ping;

            Console.WriteLine("[ViewerSim] REMOTE PING RECEIVED");
            Console.WriteLine($"  id        : {ping.Id}");
            Console.WriteLine($"  senderId  : {ping.SenderId}");
            Console.WriteLine($"  beaconId  : {ping.BeaconId}");
            Console.WriteLine(
                $"  sharedPos: ({ping.X:F3}, {ping.Y:F3}, {ping.Z:F3})");
            Console.WriteLine($"  createdAt : {ping.CreatedAt}");
            Console.WriteLine($"  total     : {receivedPings.Count}");
            Console.WriteLine();
            break;
        }

        case LynxMessageTypes.PingRemove:
        {
            var pingId = message.PingId!.Value;

            if (!receivedPings.Remove(pingId))
            {
                Console.WriteLine(
                    $"[ViewerSim] REMOVE for unknown id={pingId} ignored");
                continue;
            }

            Console.WriteLine($"[ViewerSim] REMOTE PING REMOVED id={pingId}");
            Console.WriteLine($"  total     : {receivedPings.Count}");
            Console.WriteLine();
            break;
        }
    }
}
