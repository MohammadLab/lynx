using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Lynx.Operator.Core;
using Lynx.Protocol;

namespace Lynx.Tests;

public sealed class OperatorRelayClientOverlayRecoveryTests
{
    [Fact]
    public async Task MissingOverlaySnapshotChunkTimesOutAndRequestsFreshSnapshot()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        var serverTask = Task.Run(async () =>
        {
            IPEndPoint? clientEndpoint = null;
            var snapshotRequests = 0;

            while (!testCts.IsCancellationRequested)
            {
                var received = await server.ReceiveAsync(testCts.Token);
                clientEndpoint ??= received.RemoteEndPoint;
                var type = ReadType(received.Buffer);

                if (type != OverlayMessageTypes.SnapshotRequest)
                    continue;

                snapshotRequests++;
                if (snapshotRequests == 1)
                {
                    var partial = OverlayWireCodec.SnapshotChunk(
                        snapshotId: 1,
                        stateEpoch: 100,
                        stateVersion: 0,
                        chunkIndex: 0,
                        chunkCount: 2,
                        overlays: Array.Empty<OverlayMessage>());
                    await server.SendAsync(
                        OverlayWireCodec.Serialize(partial),
                        clientEndpoint);
                    continue;
                }

                var marker = new OverlayMessage(
                    5001, 77, 0, OverlayKinds.Marker, 1,
                    new[] { new SharedPointMessage(1, 0, 2) },
                    1000, 1000);
                var complete = OverlayWireCodec.SnapshotChunk(
                    snapshotId: 2,
                    stateEpoch: 100,
                    stateVersion: 0,
                    chunkIndex: 0,
                    chunkCount: 1,
                    overlays: new[] { marker });
                await server.SendAsync(
                    OverlayWireCodec.Serialize(complete),
                    clientEndpoint);
                return snapshotRequests;
            }

            return snapshotRequests;
        }, testCts.Token);

        await using var client = new OperatorRelayClient(77);
        var recovered = new TaskCompletionSource<IReadOnlyList<OverlayMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateChanged += (overlays, _, _) =>
        {
            if (overlays.Count == 1 && overlays[0].OverlayId == 5001)
                recovered.TrySetResult(overlays);
        };

        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(testCts.Token);
        var runTask = client.RunAsync(
            new OperatorRelayClientOptions(
                RelayHost: "127.0.0.1",
                RelayPort: port,
                HeartbeatInterval: TimeSpan.FromSeconds(10),
                SnapshotTimeout: TimeSpan.FromMilliseconds(350),
                RetryDelay: TimeSpan.FromMilliseconds(100)),
            clientCts.Token);

        var state = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(state);
        Assert.Equal(5001, state[0].OverlayId);

        clientCts.Cancel();
        try { await runTask; } catch (OperationCanceledException) { }

        var requests = await serverTask;
        Assert.True(requests >= 2, $"Expected resync request, got {requests}.");
    }

    private static string? ReadType(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.TryGetProperty("type", out var type)
            ? type.GetString()
            : null;
    }
}
