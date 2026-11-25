using Lynx.Protocol;

namespace Lynx.Tests;

public sealed class RelayDiscoveryProtocolTests
{
    [Fact]
    public void ResponseRoundTripsRelayPort()
    {
        var response = RelayDiscoveryProtocol.CreateResponse(7777);

        Assert.True(
            RelayDiscoveryProtocol.TryParseResponse(response, out var port));
        Assert.Equal(7777, port);
    }

    [Theory]
    [InlineData("NOPE|7777")]
    [InlineData("LYNX_RELAY_V1|0")]
    [InlineData("LYNX_RELAY_V1|70000")]
    [InlineData("LYNX_RELAY_V1|abc")]
    public void RejectsInvalidResponses(string value)
    {
        Assert.False(
            RelayDiscoveryProtocol.TryParseResponse(value, out _));
    }
}