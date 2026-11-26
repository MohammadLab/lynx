namespace Lynx.Operator.Core;

public readonly record struct PixelPoint(double X, double Y)
{
    public static PixelPoint operator +(PixelPoint a, PixelPoint b) =>
        new(a.X + b.X, a.Y + b.Y);

    public static PixelPoint operator -(PixelPoint a, PixelPoint b) =>
        new(a.X - b.X, a.Y - b.Y);

    public static PixelPoint operator *(PixelPoint p, double scalar) =>
        new(p.X * scalar, p.Y * scalar);

    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public double Dot(PixelPoint other) => (X * other.X) + (Y * other.Y);

    public PixelPoint Normalized()
    {
        var length = Length;
        if (length <= 1e-12)
            throw new InvalidOperationException("Cannot normalize a zero-length vector.");
        return new PixelPoint(X / length, Y / length);
    }
}

public readonly record struct SharedPoint(double X, double Y, double Z);
