using Lynx.Operator.Core;
using Lynx.Protocol;

var host = "127.0.0.1";
var port = 7777;
var clientId = 501L;
var scenario = "create-remove";

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
        case "--client" when i + 1 < args.Length:
            clientId = long.Parse(args[++i]);
            break;
        case "--scenario" when i + 1 < args.Length:
            scenario = args[++i];
            break;
    }
}

await using var client = new OperatorRelayClient(clientId);
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var initialSnapshot = new TaskCompletionSource<int>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var created = new TaskCompletionSource<long>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var removed = new TaskCompletionSource<bool>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var resetAfterRestart = new TaskCompletionSource<bool>(
    TaskCreationOptions.RunContinuationsAsynchronously);
long expectedPingId = 0;
var sawNonEmptyState = false;

client.StatusChanged += status => Console.WriteLine($"[OperatorSim] {status}");
client.RelayEndpointChanged += endpoint =>
    Console.WriteLine(endpoint is null
        ? "[OperatorSim] relay disconnected"
        : $"[OperatorSim] relay {endpoint.Host}:{endpoint.Port}");
client.StateChanged += (pings, epoch, version) =>
{
    Console.WriteLine(
        $"[OperatorSim] STATE total={pings.Count} epoch={epoch} version={version}");

    initialSnapshot.TrySetResult(pings.Count);

    if (pings.Count > 0)
        sawNonEmptyState = true;

    if (expectedPingId > 0 && pings.Any(ping => ping.Id == expectedPingId))
        created.TrySetResult(expectedPingId);

    if (expectedPingId > 0 &&
        sawNonEmptyState &&
        pings.All(ping => ping.Id != expectedPingId))
        removed.TrySetResult(true);

    if (scenario == "restart-watch" && sawNonEmptyState && pings.Count == 0)
        resetAfterRestart.TrySetResult(true);
};

var runTask = client.RunAsync(
    new OperatorRelayClientOptions(
        RelayHost: host,
        RelayPort: port,
        HeartbeatInterval: TimeSpan.FromSeconds(1),
        SnapshotTimeout: TimeSpan.FromSeconds(2),
        RetryDelay: TimeSpan.FromMilliseconds(500)),
    cts.Token);

try
{
    var initialCount = await initialSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(8));
    Console.WriteLine($"[OperatorSim] INITIAL SNAPSHOT READY total={initialCount}");

    if (scenario == "snapshot-only")
    {
        Console.WriteLine($"[PASS] Operator snapshot recovery total={initialCount}.");
        return;
    }

    var ping = await client.CreateMarkerAsync(
        new SharedPoint(1.25, 0, 2.5),
        beaconId: 0,
        cts.Token);
    expectedPingId = ping.Id;
    Console.WriteLine($"[OperatorSim] CREATE SENT id={ping.Id}");

    await created.Task.WaitAsync(TimeSpan.FromSeconds(8));
    Console.WriteLine($"[OperatorSim] CREATE CONFIRMED id={ping.Id}");

    if (scenario == "restart-watch")
    {
        Console.WriteLine("[OperatorSim] WAITING FOR RELAY RESTART");
        await resetAfterRestart.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Console.WriteLine("[OperatorSim] RELAY RESTART RECOVERY CONFIRMED");
        return;
    }

    if (scenario == "seed-only")
    {
        Console.WriteLine($"[OperatorSim] SEED CONFIRMED id={ping.Id}");
        Console.WriteLine("[PASS] Operator seed state left authoritative at relay.");
        return;
    }

    await client.RemoveMarkerAsync(ping.Id, cts.Token);
    Console.WriteLine($"[OperatorSim] REMOVE SENT id={ping.Id}");
    await removed.Task.WaitAsync(TimeSpan.FromSeconds(8));
    Console.WriteLine($"[OperatorSim] REMOVE CONFIRMED id={ping.Id}");
    Console.WriteLine("[PASS] Operator relay client lifecycle passed.");
}
finally
{
    cts.Cancel();
    try { await runTask; } catch (OperationCanceledException) { }
}