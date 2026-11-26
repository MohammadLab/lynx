using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Lynx.Protocol;

var host = "127.0.0.1";
var port = 7777;
var clientId = 901L;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host" when i + 1 < args.Length: host = args[++i]; break;
        case "--port" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
        case "--client" when i + 1 < args.Length: clientId = long.Parse(args[++i]); break;
    }
}

var address = (await Dns.GetHostAddressesAsync(host))
    .First(a => a.AddressFamily == AddressFamily.InterNetwork);
var relay = new IPEndPoint(address, port);
using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));

await SendLegacy(LynxMessageCodec.RegisterClient(clientId));
await SendOverlay(OverlayWireCodec.RequestSnapshot(clientId));
var initial = await ReceiveOverlay(m => m.Type == OverlayMessageTypes.Snapshot);
Console.WriteLine($"[OverlayProbe] initial epoch={initial.StateEpoch} version={initial.StateVersion}");

const long overlayId = 8801;
var geometryV1 = new[] {
    new SharedPointMessage(0,0,0),
    new SharedPointMessage(1,0,1),
    new SharedPointMessage(2,0,1)
};

await SendOverlay(OverlayWireCodec.CreateCommand(
    overlayId, clientId, 0, OverlayKinds.Path, geometryV1,
    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
var created = await ReceiveOverlay(m =>
    m.Type == OverlayMessageTypes.Diff &&
    m.OverlayId == overlayId &&
    m.Operation == OverlayDiffOperations.Upsert);
Require(created.Revision == 1 && created.StateVersion == 1, "create did not produce revision 1/version 1");
Console.WriteLine("[OverlayProbe] create revision 1 accepted");

var geometryV2 = new[] {
    new SharedPointMessage(0,0,0),
    new SharedPointMessage(1,0,2),
    new SharedPointMessage(3,0,3)
};
await SendOverlay(OverlayWireCodec.UpdateCommand(
    overlayId, clientId, 1, OverlayKinds.Path, geometryV2));
var updated = await ReceiveOverlay(m =>
    m.Type == OverlayMessageTypes.Diff &&
    m.OverlayId == overlayId &&
    m.Operation == OverlayDiffOperations.Upsert &&
    m.Revision == 2);
Require(updated.StateVersion == 2, "update did not advance global version to 2");
Console.WriteLine("[OverlayProbe] update revision 2 accepted");

// Stale optimistic-concurrency token: relay must reject without advancing state.
await SendOverlay(OverlayWireCodec.UpdateCommand(
    overlayId, clientId, 1, OverlayKinds.Path, geometryV1));
await Task.Delay(250);
var afterStale = await Snapshot();
Require(afterStale.StateVersion == 2, "stale update advanced global state version");
Require(afterStale.Overlays!.Single(o => o.OverlayId == overlayId).Revision == 2,
    "stale update changed overlay revision");
Console.WriteLine("[OverlayProbe] stale expected revision rejected");

// Spoofed sender ID from the registered socket: must be rejected.
await SendOverlay(OverlayWireCodec.UpdateCommand(
    overlayId, clientId + 1, 2, OverlayKinds.Path, geometryV1));
await Task.Delay(250);
var afterSpoof = await Snapshot();
Require(afterSpoof.StateVersion == 2, "spoofed owner advanced state version");
Require(afterSpoof.Overlays!.Single(o => o.OverlayId == overlayId).Revision == 2,
    "spoofed owner changed overlay");
Console.WriteLine("[OverlayProbe] spoofed sender rejected");

// Invalid PATH with one point: validation must reject before mutation.
var invalid = OverlayWireCodec.CreateCommand(
    8802, clientId, 0, OverlayKinds.Path,
    new[] { new SharedPointMessage(0,0,0) },
    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
await SendRaw(OverlayWireCodec.Serialize(invalid));
await Task.Delay(250);
var afterInvalid = await Snapshot();
Require(afterInvalid.StateVersion == 2, "invalid geometry advanced state version");
Require(afterInvalid.Overlays!.All(o => o.OverlayId != 8802), "invalid geometry entered state");
Console.WriteLine("[OverlayProbe] invalid geometry rejected");

await SendOverlay(OverlayWireCodec.RemoveCommand(overlayId, clientId, 2));
var removed = await ReceiveOverlay(m =>
    m.Type == OverlayMessageTypes.Diff &&
    m.OverlayId == overlayId &&
    m.Operation == OverlayDiffOperations.Remove);
Require(removed.Revision == 3 && removed.StateVersion == 3, "remove revision/version incorrect");
Console.WriteLine("[PASS] Overlay relay rejection/revision probe passed.");

async Task<OverlayWireMessage> Snapshot()
{
    await SendOverlay(OverlayWireCodec.RequestSnapshot(clientId));
    return await ReceiveOverlay(m => m.Type == OverlayMessageTypes.Snapshot);
}

async Task SendLegacy(LynxMessage message) =>
    await SendRaw(LynxMessageCodec.Serialize(message));

async Task SendOverlay(OverlayWireMessage message) =>
    await SendRaw(OverlayWireCodec.Serialize(message));

async Task SendRaw(byte[] bytes) =>
    await udp.SendAsync(bytes, relay);

async Task<OverlayWireMessage> ReceiveOverlay(
    Func<OverlayWireMessage, bool> predicate,
    int timeoutMs = 5000)
{
    using var cts = new CancellationTokenSource(timeoutMs);
    while (true)
    {
        var received = await udp.ReceiveAsync(cts.Token);
        string? type = null;
        try
        {
            using var doc = JsonDocument.Parse(received.Buffer);
            type = doc.RootElement.GetProperty("type").GetString();
        }
        catch { }

        if (type is null || !type.StartsWith("OVERLAY_", StringComparison.Ordinal))
            continue;

        if (!OverlayWireCodec.TryParse(received.Buffer, out var message, out _ ) || message is null)
            continue;

        if (predicate(message)) return message;
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
