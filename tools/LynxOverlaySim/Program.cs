using Lynx.Operator.Core;
using Lynx.Protocol;

var host = "127.0.0.1";
var port = 7777;
var clientId = 801L;
var scenario = "types";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host" when i + 1 < args.Length: host = args[++i]; break;
        case "--port" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
        case "--client" when i + 1 < args.Length: clientId = long.Parse(args[++i]); break;
        case "--scenario" when i + 1 < args.Length: scenario = args[++i]; break;
    }
}

await using var client = new OperatorRelayClient(clientId);
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

var firstState = new TaskCompletionSource<IReadOnlyList<OverlayMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
var latestState = Array.Empty<OverlayMessage>();
var stateSignal = new SemaphoreSlim(0);

client.StatusChanged += status => Console.WriteLine($"[OverlaySim] {status}");
client.StateChanged += (overlays, epoch, version) =>
{
    latestState = overlays.ToArray();
    Console.WriteLine(
        $"[OverlaySim] STATE total={overlays.Count} epoch={epoch} version={version} " +
        $"kinds={string.Join(",", overlays.Select(o => o.Kind))}");
    firstState.TrySetResult(overlays);
    stateSignal.Release();
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
    var initial = await firstState.Task.WaitAsync(TimeSpan.FromSeconds(8));
    Console.WriteLine($"[OverlaySim] INITIAL total={initial.Count}");

    if (scenario == "snapshot-only")
    {
        Console.WriteLine($"[PASS] Overlay snapshot recovery total={initial.Count} kinds={string.Join(",", initial.Select(o => o.Kind))}");
        return;
    }

    if (scenario == "watch-lifecycle")
    {
        await WaitUntil(() =>
            latestState.Any(o => o.Kind == OverlayKinds.Marker) &&
            latestState.Any(o => o.Kind == OverlayKinds.Path) &&
            latestState.Any(o => o.Kind == OverlayKinds.Zone),
            timeoutSeconds: 15);
        Console.WriteLine("[OverlaySim] WATCH saw marker/path/zone");
        await WaitUntil(() => latestState.Length == 0, timeoutSeconds: 15);
        Console.WriteLine("[PASS] Independent overlay viewer saw generalized lifecycle.");
        return;
    }

    if (scenario == "bulk-seed")
    {
        const int count = 20;
        for (var n = 0; n < count; n++)
        {
            var points = Enumerable.Range(0, OverlayValidation.MaxGeometryPoints)
                .Select(i => new SharedPointMessage(
                    n + (i * 0.25f),
                    0,
                    i * 0.5f))
                .ToArray();
            await client.CreateOverlayAsync(
                OverlayKinds.Path, points, 0, cts.Token);
        }
        await WaitUntil(() => latestState.Length >= count, timeoutSeconds: 15);
        Console.WriteLine($"[PASS] Bulk overlay seed complete total={latestState.Length}.");
        return;
    }

    var marker = await client.CreateOverlayAsync(
        OverlayKinds.Marker,
        new[] { new SharedPointMessage(0.5f, 0f, 1f) },
        0, cts.Token);
    var path = await client.CreateOverlayAsync(
        OverlayKinds.Path,
        new[] {
            new SharedPointMessage(0f,0f,0f),
            new SharedPointMessage(1f,0f,1f),
            new SharedPointMessage(2f,0f,1.5f)
        },
        0, cts.Token);
    var zone = await client.CreateOverlayAsync(
        OverlayKinds.Zone,
        new[] {
            new SharedPointMessage(0f,0f,0f),
            new SharedPointMessage(2f,0f,0f),
            new SharedPointMessage(2f,0f,2f),
            new SharedPointMessage(0f,0f,2f)
        },
        0, cts.Token);

    await WaitUntil(() =>
        latestState.Any(o => o.OverlayId == marker.OverlayId) &&
        latestState.Any(o => o.OverlayId == path.OverlayId) &&
        latestState.Any(o => o.OverlayId == zone.OverlayId));
    Console.WriteLine("[OverlaySim] CREATE ALL CONFIRMED");

    if (scenario == "seed-only")
    {
        Console.WriteLine($"[OverlaySim] SEED marker={marker.OverlayId} path={path.OverlayId} zone={zone.OverlayId}");
        Console.WriteLine("[PASS] Generalized overlay seed state left authoritative.");
        return;
    }

    if (scenario == "restart-watch")
    {
        Console.WriteLine("[OverlaySim] WAITING FOR RELAY RESTART");
        await WaitUntil(() => latestState.Length == 0, timeoutSeconds: 18);
        Console.WriteLine("[PASS] Generalized overlay relay restart recovery confirmed.");
        return;
    }

    await client.UpdateOverlayAsync(
        path.OverlayId,
        new[] {
            new SharedPointMessage(0f,0f,0f),
            new SharedPointMessage(1f,0f,2f),
            new SharedPointMessage(3f,0f,3f)
        },
        cts.Token);
    await WaitUntil(() =>
        latestState.FirstOrDefault(o => o.OverlayId == path.OverlayId)?.Revision == 2);
    Console.WriteLine("[OverlaySim] PATH REVISION 2 CONFIRMED");

    await client.RemoveOverlayAsync(zone.OverlayId, cts.Token);
    await WaitUntil(() => latestState.All(o => o.OverlayId != zone.OverlayId));
    Console.WriteLine("[OverlaySim] ZONE REMOVE CONFIRMED");

    await client.RemoveOverlayAsync(marker.OverlayId, cts.Token);
    await client.RemoveOverlayAsync(path.OverlayId, cts.Token);
    await WaitUntil(() => latestState.Length == 0);

    Console.WriteLine("[PASS] Generalized marker/path/zone lifecycle passed.");
}
finally
{
    cts.Cancel();
    try { await runTask; } catch (OperationCanceledException) { }
}

async Task WaitUntil(Func<bool> predicate, int timeoutSeconds = 10)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (predicate()) return;
        await stateSignal.WaitAsync(TimeSpan.FromMilliseconds(500));
    }
    throw new TimeoutException("Timed out waiting for overlay state condition.");
}
