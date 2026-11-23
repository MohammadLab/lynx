using System.Net.Sockets;
using Lynx.Protocol;

var host = "127.0.0.1";
var port = 7777;
var remove = false;
long id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
long senderId = 1;
int beaconId = 0;
float x = 1.25f;
float y = -0.50f;
float z = 3.75f;
long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host" when i + 1 < args.Length:
            host = args[++i];
            break;
        case "--port" when i + 1 < args.Length:
            port = int.Parse(args[++i]);
            break;
        case "--remove":
            remove = true;
            break;
        case "--id" when i + 1 < args.Length:
            id = long.Parse(args[++i]);
            break;
        case "--sender" when i + 1 < args.Length:
            senderId = long.Parse(args[++i]);
            break;
        case "--beacon" when i + 1 < args.Length:
            beaconId = int.Parse(args[++i]);
            break;
        case "--x" when i + 1 < args.Length:
            x = float.Parse(args[++i]);
            break;
        case "--y" when i + 1 < args.Length:
            y = float.Parse(args[++i]);
            break;
        case "--z" when i + 1 < args.Length:
            z = float.Parse(args[++i]);
            break;
        case "--createdAt" when i + 1 < args.Length:
            createdAt = long.Parse(args[++i]);
            break;
    }
}

LynxMessage message;

if (remove)
{
    message = LynxMessageCodec.RemovePing(id, senderId);
}
else
{
    var ping = new PingMessage(
        id,
        senderId,
        beaconId,
        x,
        y,
        z,
        createdAt);
    message = LynxMessageCodec.CreatePing(ping);
}

var payload = LynxMessageCodec.Serialize(message);

using var udp = new UdpClient();
await udp.SendAsync(payload, payload.Length, host, port);

if (remove)
{
    Console.WriteLine(
        $"[PingSender] Sent PING_REMOVE id={id} sender={senderId} " +
        $"to {host}:{port}");
}
else
{
    var ping = message.Ping!;
    Console.WriteLine(
        $"[PingSender] Sent PING_CREATE id={ping.Id} sender={ping.SenderId} " +
        $"beacon={ping.BeaconId} " +
        $"shared=({ping.X:F2}, {ping.Y:F2}, {ping.Z:F2}) to {host}:{port}");
}
