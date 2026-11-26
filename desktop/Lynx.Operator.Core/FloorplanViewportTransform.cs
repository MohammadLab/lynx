namespace Lynx.Operator.Core;

public sealed record FloorplanViewportTransform(
    double ImageWidth,
    double ImageHeight,
    double ViewportWidth,
    double ViewportHeight,
    double Zoom,
    PixelPoint PanPixels)
{
    public double Scale
    {
        get
        {
            Validate();
            var fit = Math.Min(
                ViewportWidth / ImageWidth,
                ViewportHeight / ImageHeight);
            return fit * Zoom;
        }
    }

    public PixelPoint ImageToViewport(PixelPoint imagePixel)
    {
        var scale = Scale;
        var offset = CenterOffset(scale) + PanPixels;
        return offset + (imagePixel * scale);
    }

    public PixelPoint ViewportToImage(PixelPoint viewportPixel)
    {
        var scale = Scale;
        var offset = CenterOffset(scale) + PanPixels;
        return (viewportPixel - offset) * (1.0 / scale);
    }

    public bool ContainsImagePixel(PixelPoint imagePixel) =>
        imagePixel.X >= 0 && imagePixel.X <= ImageWidth &&
        imagePixel.Y >= 0 && imagePixel.Y <= ImageHeight;

    private PixelPoint CenterOffset(double scale)
    {
        var displayedWidth = ImageWidth * scale;
        var displayedHeight = ImageHeight * scale;
        return new PixelPoint(
            (ViewportWidth - displayedWidth) / 2.0,
            (ViewportHeight - displayedHeight) / 2.0);
    }

    private void Validate()
    {
        if (ImageWidth <= 0 || ImageHeight <= 0 ||
            ViewportWidth <= 0 || ViewportHeight <= 0)
            throw new InvalidOperationException("Image and viewport dimensions must be positive.");

        if (!double.IsFinite(Zoom) || Zoom <= 0)
            throw new InvalidOperationException("Zoom must be finite and greater than zero.");
    }
}
