using Lynx.Protocol;

namespace Lynx.Tests;

public sealed class SnapshotAssemblerTests
{
    private static PingMessage Ping(long id) =>
        new(id, 9, 0, id, 0, 0, 1000 + id);

    [Fact]
    public void AssemblesOutOfOrderChunksAndDuplicateChunk()
    {
        var assembler = new SnapshotAssembler();
        var chunk0 = LynxMessageCodec.SnapshotChunk(10, 100, 5, 0, 2, new[] { Ping(1) });
        var chunk1 = LynxMessageCodec.SnapshotChunk(10, 100, 5, 1, 2, new[] { Ping(2) });

        Assert.Equal(SnapshotAssemblyStatus.Incomplete, assembler.Add(chunk1, 0, 0).Status);
        Assert.Equal(SnapshotAssemblyStatus.Incomplete, assembler.Add(chunk1, 0, 0).Status);

        var complete = assembler.Add(chunk0, 0, 0);
        Assert.Equal(SnapshotAssemblyStatus.Complete, complete.Status);
        Assert.Equal(100L, complete.StateEpoch);
        Assert.Equal(5L, complete.StateVersion);
        Assert.Equal(new long[] { 1, 2 }, complete.Pings!.Select(p => p.Id));
    }

    [Fact]
    public void RejectsOlderSnapshotWithinSameEpoch()
    {
        var assembler = new SnapshotAssembler();
        var chunk = LynxMessageCodec.SnapshotChunk(11, 100, 4, 0, 1, new[] { Ping(1) });

        Assert.Equal(
            SnapshotAssemblyStatus.Stale,
            assembler.Add(chunk, minimumStateEpoch: 100, minimumStateVersion: 5).Status);
    }

    [Fact]
    public void NewerEpochCanResetVersionToZero()
    {
        var assembler = new SnapshotAssembler();
        var chunk = LynxMessageCodec.SnapshotChunk(12, 101, 0, 0, 1, Array.Empty<PingMessage>());

        var result = assembler.Add(chunk, minimumStateEpoch: 100, minimumStateVersion: 35);

        Assert.Equal(SnapshotAssemblyStatus.Complete, result.Status);
        Assert.Equal(101L, result.StateEpoch);
        Assert.Equal(0L, result.StateVersion);
        Assert.Empty(result.Pings!);
    }

    [Fact]
    public void OlderEpochIsRejectedEvenWithHigherVersion()
    {
        var assembler = new SnapshotAssembler();
        var chunk = LynxMessageCodec.SnapshotChunk(13, 99, 999, 0, 1, new[] { Ping(1) });

        Assert.Equal(
            SnapshotAssemblyStatus.Stale,
            assembler.Add(chunk, minimumStateEpoch: 100, minimumStateVersion: 1).Status);
    }
}