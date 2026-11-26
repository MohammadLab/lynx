using Lynx.Protocol;
using Lynx.Relay.Core;

namespace Lynx.Tests;

public sealed class OverlayStoreTests
{
    private static IReadOnlyList<SharedPointMessage> Marker(float x = 1) =>
        new[] { new SharedPointMessage(x, 0, 2) };

    [Fact]
    public void CreateAssignsRevisionOne()
    {
        var store = new OverlayStore();
        var result = store.Create(1, 10, 0, OverlayKinds.Marker, Marker(), 1000);
        Assert.Equal(OverlayMutationResult.Created, result.Result);
        Assert.Equal(1, result.Overlay!.Revision);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void UpdateAdvancesRevision()
    {
        var store = new OverlayStore();
        store.Create(1, 10, 0, OverlayKinds.Marker, Marker(), 1000);
        var result = store.Update(1, 10, 1, OverlayKinds.Marker, Marker(5), 1100);
        Assert.Equal(OverlayMutationResult.Updated, result.Result);
        Assert.Equal(2, result.Overlay!.Revision);
        Assert.Equal(5, result.Overlay.Geometry[0].X);
    }

    [Fact]
    public void StaleExpectedRevisionIsRejected()
    {
        var store = new OverlayStore();
        store.Create(1, 10, 0, OverlayKinds.Marker, Marker(), 1000);
        store.Update(1, 10, 1, OverlayKinds.Marker, Marker(2), 1100);
        var stale = store.Update(1, 10, 1, OverlayKinds.Marker, Marker(3), 1200);
        Assert.Equal(OverlayMutationResult.RevisionMismatch, stale.Result);
        Assert.Equal(2, store.Snapshot()[0].Revision);
    }

    [Fact]
    public void OwnerMismatchIsRejected()
    {
        var store = new OverlayStore();
        store.Create(1, 10, 0, OverlayKinds.Marker, Marker(), 1000);
        var result = store.Update(1, 99, 1, OverlayKinds.Marker, Marker(3), 1100);
        Assert.Equal(OverlayMutationResult.OwnerMismatch, result.Result);
    }

    [Fact]
    public void RemoveProducesNextRevision()
    {
        var store = new OverlayStore();
        store.Create(1, 10, 0, OverlayKinds.Marker, Marker(), 1000);
        var result = store.Remove(1, 10, 1);
        Assert.Equal(OverlayMutationResult.Removed, result.Result);
        Assert.Equal(2, result.RemovedRevision);
        Assert.Empty(store.Snapshot());
    }
}