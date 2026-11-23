using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lynx.Protocol;

public sealed record PingMessage(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("senderId")] long SenderId,
    [property: JsonPropertyName("beaconId")] int BeaconId,
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("z")] float Z,
    [property: JsonPropertyName("createdAt")] long CreatedAt
);

public static class PingCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(
        ReadOnlySpan<byte> utf8Json,
        out PingMessage? ping,
        out string error)
    {
        ping = null;
        error = string.Empty;

        try
        {
            ping = JsonSerializer.Deserialize<PingMessage>(utf8Json, JsonOptions);

            if (ping is null)
            {
                error = "Payload did not contain a ping.";
                return false;
            }

            return Validate(ping, out error);
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    public static bool Validate(PingMessage ping, out string error)
    {
        error = string.Empty;

        if (ping.Id <= 0)
        {
            error = "id must be greater than zero.";
            return false;
        }

        if (ping.SenderId <= 0)
        {
            error = "senderId must be greater than zero.";
            return false;
        }

        if (ping.BeaconId < 0)
        {
            error = "beaconId must be zero or greater.";
            return false;
        }

        if (ping.CreatedAt <= 0)
        {
            error = "createdAt must be greater than zero.";
            return false;
        }

        if (!float.IsFinite(ping.X) ||
            !float.IsFinite(ping.Y) ||
            !float.IsFinite(ping.Z))
        {
            error = "Coordinates must be finite.";
            return false;
        }

        return true;
    }

    public static byte[] Serialize(PingMessage ping) =>
        JsonSerializer.SerializeToUtf8Bytes(ping, JsonOptions);
}
