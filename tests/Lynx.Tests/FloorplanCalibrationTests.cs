using Lynx.Operator.Core;

namespace Lynx.Tests;

public sealed class FloorplanCalibrationTests
{
    private static FloorplanCalibration Standard() =>
        FloorplanCalibration.Create(
            originPixel: new PixelPoint(100, 100),
            xReferencePixel: new PixelPoint(200, 100),
            positiveZSidePixel: new PixelPoint(100, 200),
            xReferenceDistanceMeters: 2.0);

    [Fact]
    public void OriginMapsToSharedZero()
    {
        var calibration = Standard();
        Assert.Equal(new SharedPoint(0, 0, 0),
            calibration.PixelToShared(new PixelPoint(100, 100)));
    }

    [Fact]
    public void XReferenceMapsToKnownDistance()
    {
        var shared = Standard().PixelToShared(new PixelPoint(200, 100));
        Assert.Equal(2.0, shared.X, 8);
        Assert.Equal(0.0, shared.Z, 8);
    }

    [Fact]
    public void SelectedSideDefinesPositiveZ()
    {
        var shared = Standard().PixelToShared(new PixelPoint(100, 150));
        Assert.Equal(0.0, shared.X, 8);
        Assert.Equal(1.0, shared.Z, 8);
    }

    [Fact]
    public void ArbitraryPointMapsIntoSharedXZPlane()
    {
        var shared = Standard().PixelToShared(new PixelPoint(175, 125));
        Assert.Equal(1.5, shared.X, 8);
        Assert.Equal(0.0, shared.Y, 8);
        Assert.Equal(0.5, shared.Z, 8);
    }

    [Fact]
    public void PixelSharedPixelRoundTripIsStable()
    {
        var calibration = FloorplanCalibration.Create(
            new PixelPoint(411.2, 207.5),
            new PixelPoint(492.7, 264.1),
            new PixelPoint(370.0, 290.0),
            3.25);
        var original = new PixelPoint(625.4, 118.8);

        var restored = calibration.SharedToPixel(
            calibration.PixelToShared(original));

        Assert.Equal(original.X, restored.X, 8);
        Assert.Equal(original.Y, restored.Y, 8);
    }

    [Fact]
    public void RejectsZeroLengthXReference()
    {
        Assert.Throws<ArgumentException>(() =>
            FloorplanCalibration.Create(
                new PixelPoint(1, 1),
                new PixelPoint(1, 1),
                new PixelPoint(1, 2),
                1));
    }

    [Fact]
    public void RejectsInvalidDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FloorplanCalibration.Create(
                new PixelPoint(0, 0),
                new PixelPoint(10, 0),
                new PixelPoint(0, 10),
                0));
    }

    [Fact]
    public void RejectsCollinearZSide()
    {
        Assert.Throws<ArgumentException>(() =>
            FloorplanCalibration.Create(
                new PixelPoint(0, 0),
                new PixelPoint(10, 0),
                new PixelPoint(20, 0),
                1));
    }
}