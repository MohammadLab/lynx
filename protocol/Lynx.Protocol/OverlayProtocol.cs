using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lynx.Protocol;

public static class OverlayMessageTypes
{
    public const string CreateCommand = "OVERLAY_CREATE";
    public const string UpdateCommand = "OVERLAY_UPDATE";
    public const string RemoveCommand = "OVERLAY_REMOVE";
    public const string Diff = "OVERLAY_DIFF";
    public const string SnapshotRequest = "OVERLAY_SNAPSHOT_REQUEST";
    public const string Snapshot = "OVERLAY_SNAPSHOT";
}

public sealed record OverlayWireMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("overlayId")] long? OverlayId = null,
    [property: JsonPropertyName("senderId")] long? SenderId = null,
    [property: JsonPropertyName("clientId")] long? ClientId = null,
    [property: JsonPropertyName("beaconId")] int? BeaconId = null,
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("geometry")] IReadOnlyList<SharedPointMessage>? Geometry = null,
    [property: JsonPropertyName("expectedRevision")] long? ExpectedRevision = null,
    [property: JsonPropertyName("createdAt")] long? CreatedAt = null,
    [property: JsonPropertyName("operation")] string? Operation = null,
    [property: JsonPropertyName("revision")] long? Revision = null,
    [property: JsonPropertyName("overlay")] OverlayMessage? Overlay = null,
    [property: JsonPropertyName("stateEpoch")] long? StateEpoch = null,
    [property: JsonPropertyName("stateVersion")] long? StateVersion = null,
    [property: JsonPropertyName("snapshotId")] long? SnapshotId = null,
    [property: JsonPropertyName("chunkIndex")] int? ChunkIndex = null,
    [property: JsonPropertyName("chunkCount")] int? ChunkCount = null,
    [property: JsonPropertyName("overlays")] IReadOnlyList<OverlayMessage>? Overlays = null
);

public static class OverlayWireCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static byte[] Serialize(OverlayWireMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

    public static bool TryParse(
        ReadOnlySpan<byte> utf8Json,
        out OverlayWireMessage? message,
        out string error)
    {
        message = null;
        error = string.Empty;
        try
        {
            message = JsonSerializer.Deserialize<OverlayWireMessage>(utf8Json, JsonOptions);
            if (message is null)
            {
                error = "Payload did not contain an overlay message.";
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

    public static bool Validate(OverlayWireMessage message, out string error)
    {
        error = string.Empty;
        switch (message.Type)
        {
            case OverlayMessageTypes.CreateCommand:
                if (message.OverlayId is null or <= 0 ||
                    message.SenderId is null or <= 0 ||
                    message.BeaconId is null or < 0 ||
                    message.Kind is null ||
                    message.CreatedAt is null or <= 0)
                {
                    error = "OVERLAY_CREATE requires positive overlayId/senderId/createdAt, beaconId, kind, and geometry.";
                    return false;
                }
                return OverlayValidation.ValidateGeometry(
                    message.Kind, message.Geometry, out error);

            case OverlayMessageTypes.UpdateCommand:
                if (message.OverlayId is null or <= 0 ||
                    message.SenderId is null or <= 0 ||
                    message.ExpectedRevision is null or <= 0 ||
                    message.Kind is null)
                {
                    error = "OVERLAY_UPDATE requires overlayId/senderId/expectedRevision/kind/geometry.";
                    return false;
                }
                return OverlayValidation.ValidateGeometry(
                    message.Kind, message.Geometry, out error);

            case OverlayMessageTypes.RemoveCommand:
                if (message.OverlayId is null or <= 0 ||
                    message.SenderId is null or <= 0 ||
                    message.ExpectedRevision is null or <= 0)
                {
                    error = "OVERLAY_REMOVE requires overlayId/senderId/expectedRevision.";
                    return false;
                }
                return true;

            case OverlayMessageTypes.SnapshotRequest:
                if (message.ClientId is null or <= 0)
                {
                    error = "OVERLAY_SNAPSHOT_REQUEST requires clientId greater than zero.";
                    return false;
                }
                return true;

            case OverlayMessageTypes.Diff:
                if (message.StateEpoch is null or <= 0 ||
                    message.StateVersion is null or <= 0 ||
                    message.OverlayId is null or <= 0 ||
                    message.Revision is null or <= 0 ||
                    message.Operation is not (OverlayDiffOperations.Upsert or OverlayDiffOperations.Remove))
                {
                    error = "OVERLAY_DIFF requires authoritative epoch/version, overlayId, revision, and valid operation.";
                    return false;
                }
                if (message.Operation == OverlayDiffOperations.Upsert)
                {
                    if (message.Overlay is null)
                    {
                        error = "UPSERT diff requires overlay.";
                        return false;
                    }
                    if (message.Overlay.OverlayId != message.OverlayId ||
                        message.Overlay.Revision != message.Revision)
                    {
                        error = "Diff overlay metadata does not match envelope.";
                        return false;
                    }
                    return OverlayValidation.ValidateAuthoritative(message.Overlay, out error);
                }
                return message.Overlay is null;

            case OverlayMessageTypes.Snapshot:
                if (message.StateEpoch is null or <= 0 ||
                    message.StateVersion is null or < 0 ||
                    message.SnapshotId is null or <= 0 ||
                    message.ChunkIndex is null or < 0 ||
                    message.ChunkCount is null or <= 0 ||
                    message.ChunkIndex >= message.ChunkCount ||
                    message.Overlays is null)
                {
                    error = "OVERLAY_SNAPSHOT requires valid epoch/version/snapshot/chunk metadata and overlays.";
                    return false;
                }
                foreach (var overlay in message.Overlays)
                {
                    if (!OverlayValidation.ValidateAuthoritative(overlay, out error))
                    {
                        error = $"Invalid snapshot overlay: {error}";
                        return false;
                    }
                }
                return true;

            default:
                error = $"Unknown overlay message type '{message.Type}'.";
                return false;
        }
    }

    public static OverlayWireMessage CreateCommand(
        long overlayId, long senderId, int beaconId, string kind,
        IReadOnlyList<SharedPointMessage> geometry, long createdAt) =>
        new(
            OverlayMessageTypes.CreateCommand,
            OverlayId: overlayId,
            SenderId: senderId,
            BeaconId: beaconId,
            Kind: kind,
            Geometry: geometry,
            CreatedAt: createdAt);

    public static OverlayWireMessage UpdateCommand(
        long overlayId, long senderId, long expectedRevision, string kind,
        IReadOnlyList<SharedPointMessage> geometry) =>
        new(
            OverlayMessageTypes.UpdateCommand,
            OverlayId: overlayId,
            SenderId: senderId,
            Kind: kind,
            Geometry: geometry,
            ExpectedRevision: expectedRevision);

    public static OverlayWireMessage RemoveCommand(
        long overlayId, long senderId, long expectedRevision) =>
        new(
            OverlayMessageTypes.RemoveCommand,
            OverlayId: overlayId,
            SenderId: senderId,
            ExpectedRevision: expectedRevision);

    public static OverlayWireMessage RequestSnapshot(long clientId) =>
        new(OverlayMessageTypes.SnapshotRequest, ClientId: clientId);

    public static OverlayWireMessage UpsertDiff(
        OverlayMessage overlay, long stateEpoch, long stateVersion) =>
        new(
            OverlayMessageTypes.Diff,
            OverlayId: overlay.OverlayId,
            Operation: OverlayDiffOperations.Upsert,
            Revision: overlay.Revision,
            Overlay: overlay,
            StateEpoch: stateEpoch,
            StateVersion: stateVersion);

    public static OverlayWireMessage RemoveDiff(
        long overlayId, long revision, long stateEpoch, long stateVersion) =>
        new(
            OverlayMessageTypes.Diff,
            OverlayId: overlayId,
            Operation: OverlayDiffOperations.Remove,
            Revision: revision,
            StateEpoch: stateEpoch,
            StateVersion: stateVersion);

    public static OverlayWireMessage SnapshotChunk(
        long snapshotId, long stateEpoch, long stateVersion,
        int chunkIndex, int chunkCount,
        IReadOnlyList<OverlayMessage> overlays) =>
        new(
            OverlayMessageTypes.Snapshot,
            StateEpoch: stateEpoch,
            StateVersion: stateVersion,
            SnapshotId: snapshotId,
            ChunkIndex: chunkIndex,
            ChunkCount: chunkCount,
            Overlays: overlays);
}
