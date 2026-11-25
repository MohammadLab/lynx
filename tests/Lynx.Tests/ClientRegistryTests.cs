using System.Net;
using Lynx.Relay.Core;

namespace Lynx.Tests;

public sealed class ClientRegistryTests
{
    [Fact]
    public void RegisterOrRefresh_NewClient_AddsEndpoint()
    {
        var registry = new ClientRegistry();
        var now = DateTimeOffset.UtcNow;
        var endpoint = new IPEndPoint(IPAddress.Loopback, 8001);

        registry.RegisterOrRefresh(101, endpoint, now);

        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet(101, out var client));
        Assert.Equal(endpoint, client!.Endpoint);
        Assert.Equal(now, client.LastSeenUtc);
    }

    [Fact]
    public void RegisterOrRefresh_SameClient_ReplacesEndpointAndTimestamp()
    {
        var registry = new ClientRegistry();
        var first = new IPEndPoint(IPAddress.Loopback, 8001);
        var second = new IPEndPoint(IPAddress.Loopback, 8002);
        var t0 = DateTimeOffset.UtcNow;
        var t1 = t0.AddSeconds(5);

        registry.RegisterOrRefresh(101, first, t0);
        registry.RegisterOrRefresh(101, second, t1);

        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet(101, out var client));
        Assert.Equal(second, client!.Endpoint);
        Assert.Equal(t1, client.LastSeenUtc);
    }

    [Fact]
    public void RemoveExpired_RemovesOnlyStaleClients()
    {
        var registry = new ClientRegistry();
        var now = DateTimeOffset.UtcNow;

        registry.RegisterOrRefresh(
            101,
            new IPEndPoint(IPAddress.Loopback, 8001),
            now.AddSeconds(-30));
        registry.RegisterOrRefresh(
            102,
            new IPEndPoint(IPAddress.Loopback, 8002),
            now.AddSeconds(-5));

        var removed = registry.RemoveExpired(now, TimeSpan.FromSeconds(15));

        Assert.Equal(1, removed);
        Assert.False(registry.TryGet(101, out _));
        Assert.True(registry.TryGet(102, out _));
    }
}