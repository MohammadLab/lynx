using Lynx.Protocol;

namespace Lynx.Relay.Core;

public enum OverlayMutationResult
{
    Created,
    Updated,
    Removed,
    Duplicate,
    Conflict,
    NotFound,
    OwnerMismatch,
    RevisionMismatch,
    KindMismatch
}

public sealed record OverlayMutationOutcome(
    OverlayMutationResult Result,
    OverlayMessage? Overlay = null,
    long? RemovedRevision = null
);

public sealed class OverlayStore
{
    private readonly Dictionary<long, OverlayMessage> _overlays = new();

    public int Count => _overlays.Count;

    public IReadOnlyList<OverlayMessage> Snapshot() =>
        _overlays.Values.OrderBy(overlay => overlay.OverlayId).ToArray();

    public bool TryGet(long overlayId, out OverlayMessage? overlay) =>
        _overlays.TryGetValue(overlayId, out overlay);

    public OverlayMutationOutcome Create(
        long overlayId,
        long ownerId,
        int beaconId,
        string kind,
        IReadOnlyList<SharedPointMessage> geometry,
        long createdAt)
    {
        var candidate = new OverlayMessage(
            overlayId, ownerId, beaconId, kind, 1,
            geometry.ToArray(), createdAt, createdAt);

        if (!_overlays.TryGetValue(overlayId, out var existing))
        {
            _overlays.Add(overlayId, candidate);
            return new OverlayMutationOutcome(OverlayMutationResult.Created, candidate);
        }

        return Equivalent(existing, candidate)
            ? new OverlayMutationOutcome(OverlayMutationResult.Duplicate, existing)
            : new OverlayMutationOutcome(OverlayMutationResult.Conflict);
    }

    public OverlayMutationOutcome Update(
        long overlayId,
        long senderId,
        long expectedRevision,
        string kind,
        IReadOnlyList<SharedPointMessage> geometry,
        long updatedAt)
    {
        if (!_overlays.TryGetValue(overlayId, out var existing))
            return new OverlayMutationOutcome(OverlayMutationResult.NotFound);
        if (existing.OwnerId != senderId)
            return new OverlayMutationOutcome(OverlayMutationResult.OwnerMismatch);
        if (existing.Revision != expectedRevision)
            return new OverlayMutationOutcome(OverlayMutationResult.RevisionMismatch);
        if (existing.Kind != kind)
            return new OverlayMutationOutcome(OverlayMutationResult.KindMismatch);

        var updated = existing with
        {
            Revision = existing.Revision + 1,
            Geometry = geometry.ToArray(),
            UpdatedAt = updatedAt
        };

        _overlays[overlayId] = updated;
        return new OverlayMutationOutcome(OverlayMutationResult.Updated, updated);
    }

    public OverlayMutationOutcome Remove(
        long overlayId,
        long senderId,
        long expectedRevision)
    {
        if (!_overlays.TryGetValue(overlayId, out var existing))
            return new OverlayMutationOutcome(OverlayMutationResult.NotFound);
        if (existing.OwnerId != senderId)
            return new OverlayMutationOutcome(OverlayMutationResult.OwnerMismatch);
        if (existing.Revision != expectedRevision)
            return new OverlayMutationOutcome(OverlayMutationResult.RevisionMismatch);

        _overlays.Remove(overlayId);
        return new OverlayMutationOutcome(
            OverlayMutationResult.Removed,
            RemovedRevision: existing.Revision + 1);
    }

    private static bool Equivalent(OverlayMessage a, OverlayMessage b) =>
        a.OverlayId == b.OverlayId &&
        a.OwnerId == b.OwnerId &&
        a.BeaconId == b.BeaconId &&
        a.Kind == b.Kind &&
        a.Revision == b.Revision &&
        a.CreatedAt == b.CreatedAt &&
        a.UpdatedAt == b.UpdatedAt &&
        a.Geometry.SequenceEqual(b.Geometry);
}
