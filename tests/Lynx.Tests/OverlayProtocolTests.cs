using Lynx.Protocol;

namespace Lynx.Tests;

public sealed class OverlayProtocolTests
{
    private static IReadOnlyList<SharedPointMessage> Points(params float[] xyz)
    {
        var points = new List<SharedPointMessage>();
        for (var i = 0; i < xyz.Length; i += 3)
            points.Add(new SharedPointMessage(xyz[i], xyz[i + 1], xyz[i + 2]));
        return points;
    }

    [Theory]
    [InlineData(OverlayKinds.Marker, 1)]
    [InlineData(OverlayKinds.Path, 2)]
    [InlineData(OverlayKinds.Zone, 3)]
    public void ValidGeometryAccepted(string kind, int pointCount)
    {
        var points = Enumerable.Range(0, pointCount)
            .Select(i => new SharedPointMessage(i, 0, i))
            .ToArray();
        Assert.True(OverlayValidation.ValidateGeometry(kind, points, out var error), error);
    }

    [Fact]
    public void MarkerRejectsMultiplePoints()
    {
        Assert.False(OverlayValidation.ValidateGeometry(
            OverlayKinds.Marker,
            Points(0,0,0, 1,0,1),
            out _));
    }

    [Fact]
    public void CreateCommandRoundTrips()
    {
        var command = OverlayWireCodec.CreateCommand(
            10, 20, 0, OverlayKinds.Marker, Points(1,0,2), 1000);
        var bytes = OverlayWireCodec.Serialize(command);
        Assert.True(OverlayWireCodec.TryParse(bytes, out var parsed, out var error), error);
        Assert.Equal(OverlayMessageTypes.CreateCommand, parsed!.Type);
        Assert.Equal(10, parsed.OverlayId);
        Assert.Equal(OverlayKinds.Marker, parsed.Kind);
        Assert.Single(parsed.Geometry!);
    }

    [Fact]
    public void DiffRoundTripsAuthoritativeRevision()
    {
        var overlay = new OverlayMessage(
            10, 20, 0, OverlayKinds.Path, 4,
            Points(0,0,0, 1,0,1), 1000, 1200);
        var bytes = OverlayWireCodec.Serialize(
            OverlayWireCodec.UpsertDiff(overlay, 99, 15));
        Assert.True(OverlayWireCodec.TryParse(bytes, out var parsed, out var error), error);
        Assert.Equal(OverlayDiffOperations.Upsert, parsed!.Operation);
        Assert.Equal(4, parsed.Revision);
        Assert.Equal(15, parsed.StateVersion);
        Assert.NotNull(parsed.Overlay);
        Assert.Equal(overlay.OverlayId, parsed.Overlay!.OverlayId);
        Assert.Equal(overlay.OwnerId, parsed.Overlay.OwnerId);
        Assert.Equal(overlay.Kind, parsed.Overlay.Kind);
        Assert.Equal(overlay.Revision, parsed.Overlay.Revision);
        Assert.Equal(overlay.Geometry, parsed.Overlay.Geometry);
    }
}