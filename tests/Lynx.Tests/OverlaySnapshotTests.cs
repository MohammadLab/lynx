using Lynx.Protocol;

namespace Lynx.Tests;

public sealed class OverlaySnapshotTests
{
    private static OverlayMessage Overlay(long id, int points = 1) =>
        new(
            id, 7, 0,
            points == 1 ? OverlayKinds.Marker : OverlayKinds.Path,
            1,
            Enumerable.Range(0, points)
                .Select(i => new SharedPointMessage(i, 0, i))
                .ToArray(),
            1000 + id,
            1000 + id);

    [Fact]
    public void AssemblerHandlesOutOfOrderChunks()
    {
        var assembler = new OverlaySnapshotAssembler();
        var c0 = OverlayWireCodec.SnapshotChunk(5, 100, 9, 0, 2, new[] { Overlay(1) });
        var c1 = OverlayWireCodec.SnapshotChunk(5, 100, 9, 1, 2, new[] { Overlay(2) });
        Assert.Equal(OverlaySnapshotAssemblyStatus.Incomplete, assembler.Add(c1, 0, 0).Status);
        var result = assembler.Add(c0, 0, 0);
        Assert.Equal(OverlaySnapshotAssemblyStatus.Complete, result.Status);
        Assert.Equal(new long[] { 1, 2 }, result.Overlays!.Select(o => o.OverlayId));
    }

    [Fact]
    public void ChunkerKeepsVariableGeometryWithinBudget()
    {
        var overlays = Enumerable.Range(1, 20)
            .Select(i => Overlay(i, points: 10))
            .ToArray();
        var chunks = OverlaySnapshotChunker.ChunkBySerializedBudget(
            overlays, 9, 100, 20, 1200);
        Assert.True(chunks.Count > 1);
        Assert.Equal(overlays.Select(o => o.OverlayId), chunks.SelectMany(x => x).Select(o => o.OverlayId));
        for (var i = 0; i < chunks.Count; i++)
        {
            var bytes = OverlayWireCodec.Serialize(
                OverlayWireCodec.SnapshotChunk(9,100,20,i,chunks.Count,chunks[i]));
            Assert.True(bytes.Length <= 1200, $"chunk {i} was {bytes.Length} bytes");
        }
    }
}