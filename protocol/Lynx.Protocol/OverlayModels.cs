using System.Text.Json.Serialization;

namespace Lynx.Protocol;

public static class OverlayKinds
{
    public const string Marker = "MARKER";
    public const string Path = "PATH";
    public const string Zone = "ZONE";

    public static bool IsKnown(string kind) =>
        kind is Marker or Path or Zone;
}

public static class OverlayDiffOperations
{
    public const string Upsert = "UPSERT";
    public const string Remove = "REMOVE";
}

public sealed record SharedPointMessage(
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("z")] float Z
);

public sealed record OverlayMessage(
    [property: JsonPropertyName("overlayId")] long OverlayId,
    [property: JsonPropertyName("ownerId")] long OwnerId,
    [property: JsonPropertyName("beaconId")] int BeaconId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("geometry")] IReadOnlyList<SharedPointMessage> Geometry,
    [property: JsonPropertyName("createdAt")] long CreatedAt,
    [property: JsonPropertyName("updatedAt")] long UpdatedAt
);

public static class OverlayValidation
{
    public const int MaxGeometryPoints = 16;
    public static bool ValidateGeometry(
        string kind,
        IReadOnlyList<SharedPointMessage>? geometry,
        out string error)
    {
        error = string.Empty;

        if (!OverlayKinds.IsKnown(kind))
        {
            error = $"Unknown overlay kind '{kind}'.";
            return false;
        }

        if (geometry is null)
        {
            error = "geometry is required.";
            return false;
        }

        var minimum = kind switch
        {
            OverlayKinds.Marker => 1,
            OverlayKinds.Path => 2,
            OverlayKinds.Zone => 3,
            _ => int.MaxValue
        };

        if (kind == OverlayKinds.Marker && geometry.Count != 1)
        {
            error = "MARKER requires exactly one point.";
            return false;
        }

        if (geometry.Count < minimum)
        {
            error = $"{kind} requires at least {minimum} point(s).";
            return false;
        }

        if (geometry.Count > MaxGeometryPoints)
        {
            error = $"{kind} supports at most {MaxGeometryPoints} points in Prototype 0.6.";
            return false;
        }

        foreach (var point in geometry)
        {
            if (!float.IsFinite(point.X) ||
                !float.IsFinite(point.Y) ||
                !float.IsFinite(point.Z))
            {
                error = "Overlay coordinates must be finite.";
                return false;
            }
        }

        return true;
    }

    public static bool ValidateAuthoritative(
        OverlayMessage overlay,
        out string error)
    {
        error = string.Empty;

        if (overlay.OverlayId <= 0)
        {
            error = "overlayId must be greater than zero.";
            return false;
        }
        if (overlay.OwnerId <= 0)
        {
            error = "ownerId must be greater than zero.";
            return false;
        }
        if (overlay.BeaconId < 0)
        {
            error = "beaconId must be zero or greater.";
            return false;
        }
        if (overlay.Revision <= 0)
        {
            error = "revision must be greater than zero.";
            return false;
        }
        if (overlay.CreatedAt <= 0 || overlay.UpdatedAt <= 0)
        {
            error = "createdAt/updatedAt must be greater than zero.";
            return false;
        }
        if (overlay.UpdatedAt < overlay.CreatedAt)
        {
            error = "updatedAt must not precede createdAt.";
            return false;
        }

        return ValidateGeometry(overlay.Kind, overlay.Geometry, out error);
    }
}
