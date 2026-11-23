using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lynx.Protocol;

public static class LynxMessageTypes
{
    public const string PingCreate = "PING_CREATE";
    public const string PingRemove = "PING_REMOVE";
}

public sealed record LynxMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("ping")] PingMessage? Ping = null,
    [property: JsonPropertyName("pingId")] long? PingId = null,
    [property: JsonPropertyName("senderId")] long? SenderId = null
);

public static class LynxMessageCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(
        ReadOnlySpan<byte> utf8Json,
        out LynxMessage? message,
        out string error)
    {
        message = null;
        error = string.Empty;

        try
        {
            message = JsonSerializer.Deserialize<LynxMessage>(utf8Json, JsonOptions);

            if (message is null)
            {
                error = "Payload did not contain a message.";
                return false;
            }

            return Validate(message, out error);
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    public static bool Validate(LynxMessage message, out string error)
    {
        error = string.Empty;

        switch (message.Type)
        {
            case LynxMessageTypes.PingCreate:
                if (message.Ping is null)
                {
                    error = "PING_CREATE requires ping.";
                    return false;
                }

                return PingCodec.Validate(message.Ping, out error);

            case LynxMessageTypes.PingRemove:
                if (message.PingId is null or <= 0)
                {
                    error = "PING_REMOVE requires pingId greater than zero.";
                    return false;
                }

                if (message.SenderId is null or <= 0)
                {
                    error = "PING_REMOVE requires senderId greater than zero.";
                    return false;
                }

                return true;

            default:
                error = $"Unknown message type '{message.Type}'.";
                return false;
        }
    }

    public static byte[] Serialize(LynxMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

    public static LynxMessage CreatePing(PingMessage ping) =>
        new(LynxMessageTypes.PingCreate, Ping: ping);

    public static LynxMessage RemovePing(long pingId, long senderId) =>
        new(LynxMessageTypes.PingRemove, PingId: pingId, SenderId: senderId);
}
