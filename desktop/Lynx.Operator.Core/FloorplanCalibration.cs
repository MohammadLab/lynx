namespace Lynx.Operator.Core;

public sealed record FloorplanCalibration(
    PixelPoint OriginPixel,
    PixelPoint XAxisPixel,
    PixelPoint ZAxisPixel,
    double MetersPerPixel)
{
    public static FloorplanCalibration Create(
        PixelPoint originPixel,
        PixelPoint xReferencePixel,
        PixelPoint positiveZSidePixel,
        double xReferenceDistanceMeters)
    {
        if (!double.IsFinite(xReferenceDistanceMeters) ||
            xReferenceDistanceMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(xReferenceDistanceMeters),
                "Reference distance must be finite and greater than zero.");
        }

        var xVector = xReferencePixel - originPixel;
        var xPixels = xVector.Length;
        if (xPixels <= 1e-6)
        {
            throw new ArgumentException(
                "The +X reference must be a different pixel from the origin.",
                nameof(xReferencePixel));
        }

        var xAxis = xVector.Normalized();
        var candidateZ = new PixelPoint(-xAxis.Y, xAxis.X);
        var zSideVector = positiveZSidePixel - originPixel;
        var zProjection = candidateZ.Dot(zSideVector);

        if (Math.Abs(zProjection) <= 1e-6)
        {
            throw new ArgumentException(
                "The +Z side reference is collinear with the +X axis.",
                nameof(positiveZSidePixel));
        }

        var zAxis = zProjection > 0
            ? candidateZ
            : candidateZ * -1;

        var metersPerPixel = xReferenceDistanceMeters / xPixels;
        if (!double.IsFinite(metersPerPixel) || metersPerPixel <= 0)
            throw new InvalidOperationException("Derived scale is invalid.");

        return new FloorplanCalibration(
            originPixel,
            xAxis,
            zAxis,
            metersPerPixel);
    }

    public SharedPoint PixelToShared(PixelPoint pixel)
    {
        var delta = pixel - OriginPixel;
        var x = delta.Dot(XAxisPixel) * MetersPerPixel;
        var z = delta.Dot(ZAxisPixel) * MetersPerPixel;
        return new SharedPoint(x, 0, z);
    }

    public PixelPoint SharedToPixel(SharedPoint shared)
    {
        if (!double.IsFinite(shared.X) ||
            !double.IsFinite(shared.Y) ||
            !double.IsFinite(shared.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(shared));
        }

        var xPixels = shared.X / MetersPerPixel;
        var zPixels = shared.Z / MetersPerPixel;
        return OriginPixel +
            (XAxisPixel * xPixels) +
            (ZAxisPixel * zPixels);
    }
}
