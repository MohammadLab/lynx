using System.Net;
using System.Net.Sockets;
using Lynx.Protocol;

var listenPort = 7778;
var expectedBeaconId = 0;
var clientId = 101L;
var relayEndpoint = new IPEndPoint(IPAddress.Loopback, 7777);
var heartbeatInterval = TimeSpan.FromSeconds(5);

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

        case "--client" when i + 1 < args.Length:
            clientId = long.Parse(args[++i]);
            break;

        case "--relay" when i + 1 < args.Length:
            relayEndpoint = ParseEndpoint(args[++i]);
            break;

        case "--heartbeat-seconds" when i + 1 < args.Length:
            heartbeatInterval = TimeSpan.FromSeconds(int.Parse(args[++i]));
            break;
    }
}

var receivedPings = new Dictionary<long, PingMessage>();
var snapshotAssembler = new SnapshotAssembler();
long appliedEpoch = 0;
long appliedVersion = 0;
long? pendingSnapshotId = null;
DateTimeOffset? snapshotDeadlineUtc = null;
var snapshotTimeout = TimeSpan.FromSeconds(3);
using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"[ViewerSim] Listening on UDP :{listenPort}");
Console.WriteLine($"[ViewerSim] Client ID: {clientId}");
Console.WriteLine($"[ViewerSim] Relay: {relayEndpoint}");
Console.WriteLine($"[ViewerSim] Shared frame beaconId={expectedBeaconId}");
Console.WriteLine("[ViewerSim] Ctrl+C to stop.");
Console.WriteLine();

await SendToRelay(LynxMessageCodec.RegisterClient(clientId));
Console.WriteLine("[ViewerSim] REGISTER sent.");

var maintenanceTask = Task.Run(async () =>
{
    var nextHeartbeatUtc = DateTimeOffset.UtcNow + heartbeatInterval;

    while (!cts.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(250, cts.Token);
            var now = DateTimeOffset.UtcNow;

            if (now >= nextHeartbeatUtc)
            {
                await SendToRelay(LynxMessageCodec.Heartbeat(clientId));
                nextHeartbeatUtc = now + heartbeatInterval;
            }

            if (snapshotDeadlineUtc is not null && now >= snapshotDeadlineUtc)
            {
                Console.WriteLine("[ViewerSim] SNAPSHOT timeout; requesting fresh snapshot");
                snapshotAssembler.Reset();
                pendingSnapshotId = null;
                snapshotDeadlineUtc = null;
                await SendToRelay(LynxMessageCodec.RequestSnapshot(clientId));
            }
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
});

try
{
    while (!cts.IsCancellationRequested)
    {
        UdpReceiveResult received;

        try
        {
            received = await udp.ReceiveAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (SocketException ex)
        {
            Console.WriteLine(
                $"[ViewerSim] UDP receive transient error {ex.SocketErrorCode}; continuing");
            await Task.Delay(100, cts.Token);
            continue;
        }

        if (!LynxMessageCodec.TryParse(
                received.Buffer,
                out var message,
                out var error) ||
            message is null)
        {
            Console.WriteLine(
                $"[ViewerSim] DROP from {received.RemoteEndPoint}: {error}");
            continue;
        }

        switch (message.Type)
        {
            case LynxMessageTypes.Snapshot:
            {
                var result = snapshotAssembler.Add(message, appliedEpoch, appliedVersion);

                if (pendingSnapshotId != message.SnapshotId)
                {
                    pendingSnapshotId = message.SnapshotId;
                    snapshotDeadlineUtc = DateTimeOffset.UtcNow + snapshotTimeout;
                }

                if (result.Status == SnapshotAssemblyStatus.Stale)
                {
                    Console.WriteLine(
                        $"[ViewerSim] STALE snapshot ignored version={message.StateVersion}");
                    break;
                }

                if (result.Status == SnapshotAssemblyStatus.Conflict)
                {
                    Console.WriteLine("[ViewerSim] SNAPSHOT conflict; requesting fresh snapshot");
                    snapshotAssembler.Reset();
                    pendingSnapshotId = null;
                    snapshotDeadlineUtc = null;
                    await SendToRelay(LynxMessageCodec.RequestSnapshot(clientId));
                    break;
                }

                if (result.Status != SnapshotAssemblyStatus.Complete)
                {
                    Console.WriteLine(
                        $"[ViewerSim] SNAPSHOT chunk {message.ChunkIndex! + 1}/{message.ChunkCount} " +
                        $"version={message.StateVersion}");
                    break;
                }

                pendingSnapshotId = null;
                snapshotDeadlineUtc = null;
                receivedPings.Clear();
                foreach (var ping in result.Pings!)
                {
                    if (ping.BeaconId == expectedBeaconId)
                        receivedPings[ping.Id] = ping;
                }

                appliedEpoch = result.StateEpoch!.Value;
                appliedVersion = result.StateVersion!.Value;
                Console.WriteLine(
                    $"[ViewerSim] SNAPSHOT applied total={receivedPings.Count} " +
                    $"epoch={appliedEpoch} version={appliedVersion}");
                break;
            }

            case LynxMessageTypes.PingCreate:
            {
                var epoch = message.StateEpoch;
                var version = message.StateVersion;
                if (epoch is not null && version is not null)
                {
                    if (epoch < appliedEpoch ||
                        (epoch == appliedEpoch && version <= appliedVersion))
                    {
                        Console.WriteLine(
                            $"[ViewerSim] STALE create ignored epoch={epoch} version={version}");
                        break;
                    }
                    if (epoch > appliedEpoch || version > appliedVersion + 1)
                    {
                        Console.WriteLine(
                            $"[ViewerSim] VERSION GAP local={appliedEpoch}:{appliedVersion} " +
                            $"received={epoch}:{version}; requesting snapshot");
                        snapshotAssembler.Reset();
                        await SendToRelay(LynxMessageCodec.RegisterClient(clientId));
                        break;
                    }
                }

                if (epoch is not null && version is not null)
                {
                    appliedEpoch = epoch.Value;
                    appliedVersion = version.Value;
                }

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
                var epoch = message.StateEpoch;
                var version = message.StateVersion;
                if (epoch is not null && version is not null)
                {
                    if (epoch < appliedEpoch ||
                        (epoch == appliedEpoch && version <= appliedVersion))
                    {
                        Console.WriteLine(
                            $"[ViewerSim] STALE remove ignored epoch={epoch} version={version}");
                        break;
                    }
                    if (epoch > appliedEpoch || version > appliedVersion + 1)
                    {
                        Console.WriteLine(
                            $"[ViewerSim] VERSION GAP local={appliedEpoch}:{appliedVersion} " +
                            $"received={epoch}:{version}; requesting snapshot");
                        snapshotAssembler.Reset();
                        await SendToRelay(LynxMessageCodec.RegisterClient(clientId));
                        break;
                    }
                }

                if (epoch is not null && version is not null)
                {
                    appliedEpoch = epoch.Value;
                    appliedVersion = version.Value;
                }

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
}
finally
{
    cts.Cancel();
    await maintenanceTask;
}

async Task SendToRelay(LynxMessage message)
{
    var bytes = LynxMessageCodec.Serialize(message);
    await udp.SendAsync(bytes, relayEndpoint);
}

static IPEndPoint ParseEndpoint(string value)
{
    var split = value.LastIndexOf(':');
    if (split <= 0 || split == value.Length - 1)
        throw new ArgumentException($"Bad endpoint '{value}'. Expected ip:port.");

    return new IPEndPoint(
        IPAddress.Parse(value[..split]),
        int.Parse(value[(split + 1)..]));
}