using Lynx.Operator.Core;

namespace Lynx.Tests;

public sealed class FloorplanViewportTransformTests
{
    [Fact]
    public void AspectFitCentersImage()
    {
        var transform = new FloorplanViewportTransform(
            ImageWidth: 1000,
            ImageHeight: 500,
            ViewportWidth: 1000,
            ViewportHeight: 1000,
            Zoom: 1,
            PanPixels: new PixelPoint(0, 0));

        var topLeft = transform.ImageToViewport(new PixelPoint(0, 0));
        Assert.Equal(0, topLeft.X, 8);
        Assert.Equal(250, topLeft.Y, 8);
    }

    [Fact]
    public void ZoomAndPanRoundTripDoesNotChangeImageCoordinate()
    {
        var transform = new FloorplanViewportTransform(
            1920, 1080, 1400, 900, 2.35, new PixelPoint(137, -82));
        var imagePoint = new PixelPoint(731.25, 442.75);

        var viewport = transform.ImageToViewport(imagePoint);
        var restored = transform.ViewportToImage(viewport);

        Assert.Equal(imagePoint.X, restored.X, 8);
        Assert.Equal(imagePoint.Y, restored.Y, 8);
    }

    [Fact]
    public void ChangingViewportDoesNotChangeStoredImageCoordinate()
    {
        var imagePoint = new PixelPoint(300, 200);
        var a = new FloorplanViewportTransform(
            1000, 800, 1200, 900, 1, new PixelPoint(0, 0));
        var b = new FloorplanViewportTransform(
            1000, 800, 700, 500, 3, new PixelPoint(-40, 75));

        Assert.Equal(imagePoint, a.ViewportToImage(a.ImageToViewport(imagePoint)));
        Assert.Equal(imagePoint, b.ViewportToImage(b.ImageToViewport(imagePoint)));
    }
}