using Lynx.Protocol;

namespace Lynx.Tests;

public sealed class CrossPlatformOverlayContractTests
{
    [Fact]
    public void SharedPathDiffFixtureParsesInCSharp()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "overlay-contract-v1.json");
        var bytes = File.ReadAllBytes(path);

        Assert.True(
            OverlayWireCodec.TryParse(bytes, out var message, out var error),
            error);
        Assert.NotNull(message);
        Assert.Equal(OverlayMessageTypes.Diff, message!.Type);
        Assert.Equal(42, message.OverlayId);
        Assert.Equal(3, message.Revision);
        Assert.Equal(OverlayKinds.Path, message.Overlay!.Kind);
        Assert.Equal(3, message.Overlay.Geometry.Count);
        Assert.Equal(900, message.StateEpoch);
        Assert.Equal(12, message.StateVersion);
    }
}
