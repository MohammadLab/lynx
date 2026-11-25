using Lynx.Protocol;

namespace Lynx.Tests;

public sealed class SnapshotChunkerTests
{
    [Fact]
    public void ChunkBySerializedBudget_StaysWithinBudgetAndPreservesOrder()
    {
        var pings = Enumerable.Range(1, 100)
            .Select(i => new PingMessage(
                i,
                99,
                0,
                i * 1.25f,
                i * -0.5f,
                i * 3.75f,
                1_800_000_000_000L + i))
            .ToArray();

        const int budget = 1200;
        var chunks = SnapshotChunker.ChunkBySerializedBudget(
            pings,
            snapshotId: 42,
            stateEpoch: 100,
            stateVersion: 100,
            maxPayloadBytes: budget);

        Assert.True(chunks.Count > 1);
        Assert.Equal(pings.Select(p => p.Id), chunks.SelectMany(c => c).Select(p => p.Id));

        for (var i = 0; i < chunks.Count; i++)
        {
            var bytes = LynxMessageCodec.Serialize(
                LynxMessageCodec.SnapshotChunk(
                    42,
                    100,
                    100,
                    i,
                    chunks.Count,
                    chunks[i]));

            Assert.True(
                bytes.Length <= budget,
                $"Chunk {i} serialized to {bytes.Length} bytes, over {budget}.");
        }
    }
}