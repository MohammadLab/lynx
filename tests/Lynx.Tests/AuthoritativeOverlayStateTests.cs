using Lynx.Operator.Core;
using Lynx.Protocol;

namespace Lynx.Tests;

public sealed class AuthoritativeOverlayStateTests
{
    private static OverlayMessage Marker(long id, long revision = 1) =>
        new(
            id, 9, 0, OverlayKinds.Marker, revision,
            new[] { new SharedPointMessage(id, 0, id) },
            1000 + id,
            1000 + id + revision - 1);

    [Fact]
    public void SnapshotThenContiguousDiffApplies()
    {
        var state = new AuthoritativeOverlayState();
        var snapshot = OverlayWireCodec.SnapshotChunk(
            1, 100, 1, 0, 1, new[] { Marker(1) });
        Assert.Equal(OverlayStateApplyStatus.SnapshotApplied, state.Apply(snapshot).Status);

        var diff = OverlayWireCodec.UpsertDiff(Marker(2), 100, 2);
        Assert.Equal(OverlayStateApplyStatus.Applied, state.Apply(diff).Status);
        Assert.Equal(new long[] { 1, 2 }, state.Snapshot().Select(o => o.OverlayId));
    }

    [Fact]
    public void PerOverlayStaleRevisionDoesNotRollGeometryBackButAdvancesGlobalVersion()
    {
        var state = new AuthoritativeOverlayState();
        state.Apply(OverlayWireCodec.SnapshotChunk(
            1, 100, 5, 0, 1, new[] { Marker(1, revision: 3) }));

        var staleOverlay = Marker(1, revision: 2);
        var result = state.Apply(OverlayWireCodec.UpsertDiff(staleOverlay, 100, 6));

        Assert.Equal(OverlayStateApplyStatus.Applied, result.Status);
        Assert.False(result.StateChanged);
        Assert.Equal(3, state.Snapshot().Single().Revision);
        Assert.Equal(6, state.AppliedVersion);
    }

    [Fact]
    public void RemoveTombstoneBlocksOlderOverlayRevision()
    {
        var state = new AuthoritativeOverlayState();
        state.Apply(OverlayWireCodec.SnapshotChunk(
            1, 100, 1, 0, 1, new[] { Marker(1, revision: 1) }));
        state.Apply(OverlayWireCodec.RemoveDiff(1, 2, 100, 2));
        Assert.Empty(state.Snapshot());
        Assert.Equal(2, state.LatestRevision(1));

        var delayed = state.Apply(OverlayWireCodec.UpsertDiff(Marker(1, revision: 1), 100, 3));
        Assert.Equal(OverlayStateApplyStatus.Applied, delayed.Status);
        Assert.False(delayed.StateChanged);
        Assert.Empty(state.Snapshot());
    }

    [Fact]
    public void GlobalVersionGapRequiresResync()
    {
        var state = new AuthoritativeOverlayState();
        state.Apply(OverlayWireCodec.SnapshotChunk(1, 100, 1, 0, 1, Array.Empty<OverlayMessage>()));
        var result = state.Apply(OverlayWireCodec.UpsertDiff(Marker(1), 100, 3));
        Assert.Equal(OverlayStateApplyStatus.ResyncRequired, result.Status);
        Assert.Equal(1, state.AppliedVersion);
    }

    [Fact]
    public void NewEpochSnapshotCanResetVersion()
    {
        var state = new AuthoritativeOverlayState();
        state.Apply(OverlayWireCodec.SnapshotChunk(1, 100, 50, 0, 1, new[] { Marker(1) }));
        var result = state.Apply(OverlayWireCodec.SnapshotChunk(2, 101, 0, 0, 1, Array.Empty<OverlayMessage>()));
        Assert.Equal(OverlayStateApplyStatus.SnapshotApplied, result.Status);
        Assert.Equal(101, state.AppliedEpoch);
        Assert.Equal(0, state.AppliedVersion);
        Assert.Empty(state.Snapshot());
    }
}