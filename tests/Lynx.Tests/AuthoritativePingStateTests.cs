using Lynx.Operator.Core;
using Lynx.Protocol;

namespace Lynx.Tests;

public sealed class AuthoritativePingStateTests
{
    private static PingMessage Ping(long id, long sender = 9) =>
        new(id, sender, 0, id, 0, id * 2, 1000 + id);

    [Fact]
    public void InitialMutationRequiresSnapshot()
    {
        var state = new AuthoritativePingState();
        var result = state.Apply(LynxMessageCodec.CreatePing(Ping(1), 100, 1));
        Assert.Equal(StateApplyStatus.ResyncRequired, result.Status);
    }

    [Fact]
    public void SnapshotThenContiguousMutationApplies()
    {
        var state = new AuthoritativePingState();
        var snapshot = LynxMessageCodec.SnapshotChunk(1, 100, 1, 0, 1, new[] { Ping(1) });

        Assert.Equal(StateApplyStatus.SnapshotApplied, state.Apply(snapshot).Status);
        Assert.Equal(100, state.AppliedEpoch);
        Assert.Equal(1, state.AppliedVersion);

        var create = state.Apply(LynxMessageCodec.CreatePing(Ping(2), 100, 2));
        Assert.Equal(StateApplyStatus.Applied, create.Status);
        Assert.Equal(new long[] { 1, 2 }, state.Snapshot().Select(p => p.Id));
    }

    [Fact]
    public void VersionGapRequestsResyncWithoutApplying()
    {
        var state = new AuthoritativePingState();
        state.Apply(LynxMessageCodec.SnapshotChunk(1, 100, 1, 0, 1, new[] { Ping(1) }));

        var result = state.Apply(LynxMessageCodec.CreatePing(Ping(3), 100, 3));

        Assert.Equal(StateApplyStatus.ResyncRequired, result.Status);
        Assert.DoesNotContain(state.Snapshot(), ping => ping.Id == 3);
        Assert.Equal(1, state.AppliedVersion);
    }

    [Fact]
    public void StaleMutationIsIgnored()
    {
        var state = new AuthoritativePingState();
        state.Apply(LynxMessageCodec.SnapshotChunk(1, 100, 2, 0, 1, new[] { Ping(1) }));

        var result = state.Apply(LynxMessageCodec.CreatePing(Ping(2), 100, 2));

        Assert.Equal(StateApplyStatus.StaleIgnored, result.Status);
        Assert.Single(state.Snapshot());
    }

    [Fact]
    public void NewerEpochSnapshotReplacesOlderState()
    {
        var state = new AuthoritativePingState();
        state.Apply(LynxMessageCodec.SnapshotChunk(1, 100, 5, 0, 1, new[] { Ping(1), Ping(2) }));

        var result = state.Apply(LynxMessageCodec.SnapshotChunk(2, 101, 0, 0, 1, Array.Empty<PingMessage>()));

        Assert.Equal(StateApplyStatus.SnapshotApplied, result.Status);
        Assert.Empty(state.Snapshot());
        Assert.Equal(101, state.AppliedEpoch);
        Assert.Equal(0, state.AppliedVersion);
    }

    [Fact]
    public void RemoveAdvancesVersionEvenWhenObjectWasUnknown()
    {
        var state = new AuthoritativePingState();
        state.Apply(LynxMessageCodec.SnapshotChunk(1, 100, 1, 0, 1, Array.Empty<PingMessage>()));

        var result = state.Apply(LynxMessageCodec.RemovePing(99, 9, 100, 2));

        Assert.Equal(StateApplyStatus.Applied, result.Status);
        Assert.False(result.StateChanged);
        Assert.Equal(2, state.AppliedVersion);
    }
}