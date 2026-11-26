using Lynx.Operator.Core;

namespace Lynx.Tests;

public sealed class CrossPlatformSpatialContractTests
{
    [Fact]
    public void DesktopFloorplanClickMatchesSharedContractFixture()
    {
        var values = LoadFixture();

        var calibration = FloorplanCalibration.Create(
            ParsePixel(values["floor.origin"]),
            ParsePixel(values["floor.xref"]),
            ParsePixel(values["floor.zside"]),
            double.Parse(values["floor.xdistance"], System.Globalization.CultureInfo.InvariantCulture));

        var actual = calibration.PixelToShared(ParsePixel(values["floor.click"]));
        var expected = ParseShared(values["shared.expected"]);

        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
        Assert.Equal(expected.Z, actual.Z, 8);
    }

    private static Dictionary<string, string> LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "spatial-contract-v1.properties");
        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());
    }

    private static PixelPoint ParsePixel(string value)
    {
        var parts = value.Split(',');
        return new PixelPoint(Parse(parts[0]), Parse(parts[1]));
    }

    private static SharedPoint ParseShared(string value)
    {
        var parts = value.Split(',');
        return new SharedPoint(Parse(parts[0]), Parse(parts[1]), Parse(parts[2]));
    }

    private static double Parse(string value) =>
        double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
