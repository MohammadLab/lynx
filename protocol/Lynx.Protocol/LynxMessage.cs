using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lynx.Protocol;

public static class LynxMessageTypes
{
    public const string PingCreate = "PING_CREATE";
    public const string PingRemove = "PING_REMOVE";
    public const string ClientRegister = "CLIENT_REGISTER";
    public const string ClientHeartbeat = "CLIENT_HEARTBEAT";
    public const string Snapshot = "SNAPSHOT";
}

public sealed record LynxMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("ping")] PingMessage? Ping = null,
    [property: JsonPropertyName("pingId")] long? PingId = null,
    [property: JsonPropertyName("senderId")] long? SenderId = null,
    [property: JsonPropertyName("clientId")] long? ClientId = null,
    [property: JsonPropertyName("stateEpoch")] long? StateEpoch = null,
    [property: JsonPropertyName("stateVersion")] long? StateVersion = null,
    [property: JsonPropertyName("snapshotId")] long? SnapshotId = null,
    [property: JsonPropertyName("chunkIndex")] int? ChunkIndex = null,
    [property: JsonPropertyName("chunkCount")] int? ChunkCount = null,
    [property: JsonPropertyName("pings")] IReadOnlyList<PingMessage>? Pings = null
);

public static class LynxMessageCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(ReadOnlySpan<byte> utf8Json, out LynxMessage? message, out string error)
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
                if (message.StateVersion is not null)
                {
                    if (message.StateVersion <= 0 || message.StateEpoch is null or <= 0)
                    {
                        error = "PING_CREATE authoritative version requires positive stateEpoch/stateVersion.";
                        return false;
                    }
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
                if (message.StateVersion is not null)
                {
                    if (message.StateVersion <= 0 || message.StateEpoch is null or <= 0)
                    {
                        error = "PING_REMOVE authoritative version requires positive stateEpoch/stateVersion.";
                        return false;
                    }
                }
                return true;

            case LynxMessageTypes.ClientRegister:
            case LynxMessageTypes.ClientHeartbeat:
                if (message.ClientId is null or <= 0)
                {
                    error = $"{message.Type} requires clientId greater than zero.";
                    return false;
                }
                return true;

            case LynxMessageTypes.Snapshot:
                if (message.StateEpoch is null or <= 0)
                {
                    error = "SNAPSHOT requires stateEpoch greater than zero.";
                    return false;
                }
                if (message.StateVersion is null or < 0)
                {
                    error = "SNAPSHOT requires stateVersion zero or greater.";
                    return false;
                }
                if (message.SnapshotId is null or <= 0)
                {
                    error = "SNAPSHOT requires snapshotId greater than zero.";
                    return false;
                }
                if (message.ChunkIndex is null or < 0)
                {
                    error = "SNAPSHOT requires chunkIndex zero or greater.";
                    return false;
                }
                if (message.ChunkCount is null or <= 0 || message.ChunkIndex >= message.ChunkCount)
                {
                    error = "SNAPSHOT requires valid chunkCount/chunkIndex.";
                    return false;
                }
                if (message.Pings is null)
                {
                    error = "SNAPSHOT requires pings.";
                    return false;
                }
                foreach (var ping in message.Pings)
                {
                    if (!PingCodec.Validate(ping, out error))
                    {
                        error = $"Invalid snapshot ping: {error}";
                        return false;
                    }
                }
                return true;

            default:
                error = $"Unknown message type '{message.Type}'.";
                return false;
        }
    }

    public static byte[] Serialize(LynxMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

    public static LynxMessage CreatePing(
        PingMessage ping,
        long? stateEpoch = null,
        long? stateVersion = null) =>
        new(
            LynxMessageTypes.PingCreate,
            Ping: ping,
            StateEpoch: stateEpoch,
            StateVersion: stateVersion);

    public static LynxMessage RemovePing(
        long pingId,
        long senderId,
        long? stateEpoch = null,
        long? stateVersion = null) =>
        new(
            LynxMessageTypes.PingRemove,
            PingId: pingId,
            SenderId: senderId,
            StateEpoch: stateEpoch,
            StateVersion: stateVersion);

    public static LynxMessage RegisterClient(long clientId) =>
        new(LynxMessageTypes.ClientRegister, ClientId: clientId);

    public static LynxMessage Heartbeat(long clientId) =>
        new(LynxMessageTypes.ClientHeartbeat, ClientId: clientId);

    public static LynxMessage SnapshotChunk(
        long snapshotId,
        long stateEpoch,
        long stateVersion,
        int chunkIndex,
        int chunkCount,
        IReadOnlyList<PingMessage> pings) =>
        new(
            LynxMessageTypes.Snapshot,
            StateEpoch: stateEpoch,
            StateVersion: stateVersion,
            SnapshotId: snapshotId,
            ChunkIndex: chunkIndex,
            ChunkCount: chunkCount,
            Pings: pings);
}