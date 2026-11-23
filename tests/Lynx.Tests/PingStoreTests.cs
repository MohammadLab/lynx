using Lynx.Protocol;
using Lynx.Relay.Core;

namespace Lynx.Tests;

public sealed class PingStoreTests
{
    private static PingMessage MakePing(
        long id = 10,
        long senderId = 1,
        float x = 1.0f) =>
        new(
            Id: id,
            SenderId: senderId,
            BeaconId: 0,
            X: x,
            Y: 2.0f,
            Z: 3.0f,
            CreatedAt: 1000);

    [Fact]
    public void TryAdd_NewId_AddsPing()
    {
        var store = new PingStore();
        var ping = MakePing();

        var result = store.TryAdd(ping);

        Assert.Equal(PingAddResult.Added, result);
        Assert.Equal(1, store.Count);
        Assert.True(store.TryGet(ping.Id, out var stored));
        Assert.Equal(ping, stored);
    }

    [Fact]
    public void TryAdd_ExactRepeat_IsDuplicate()
    {
        var store = new PingStore();
        var ping = MakePing();

        Assert.Equal(PingAddResult.Added, store.TryAdd(ping));
        Assert.Equal(PingAddResult.Duplicate, store.TryAdd(ping));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryAdd_SameIdDifferentContents_IsConflict()
    {
        var store = new PingStore();
        var original = MakePing(id: 20, x: 1.0f);
        var conflicting = MakePing(id: 20, x: 99.0f);

        Assert.Equal(PingAddResult.Added, store.TryAdd(original));
        Assert.Equal(PingAddResult.Conflict, store.TryAdd(conflicting));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryRemove_Owner_RemovesPing()
    {
        var store = new PingStore();
        var ping = MakePing(id: 30, senderId: 5);

        store.TryAdd(ping);

        var result = store.TryRemove(30, 5);

        Assert.Equal(PingRemoveResult.Removed, result);
        Assert.Equal(0, store.Count);
        Assert.False(store.TryGet(30, out _));
    }

    [Fact]
    public void TryRemove_AlreadyRemoved_IsIdempotentNotFound()
    {
        var store = new PingStore();
        var ping = MakePing(id: 40, senderId: 5);

        store.TryAdd(ping);

        Assert.Equal(PingRemoveResult.Removed, store.TryRemove(40, 5));
        Assert.Equal(PingRemoveResult.NotFound, store.TryRemove(40, 5));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void TryRemove_DifferentSender_IsRejected()
    {
        var store = new PingStore();
        var ping = MakePing(id: 50, senderId: 5);

        store.TryAdd(ping);

        var result = store.TryRemove(50, 99);

        Assert.Equal(PingRemoveResult.SenderMismatch, result);
        Assert.Equal(1, store.Count);
        Assert.True(store.TryGet(50, out var stored));
        Assert.Equal(ping, stored);
    }
}
