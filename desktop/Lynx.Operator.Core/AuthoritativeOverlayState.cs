using Lynx.Protocol;

namespace Lynx.Operator.Core;

public enum OverlayStateApplyStatus
{
    Applied,
    SnapshotIncomplete,
    SnapshotApplied,
    StaleIgnored,
    ResyncRequired,
    Ignored
}

public sealed record OverlayStateApplyResult(
    OverlayStateApplyStatus Status,
    bool StateChanged = false
);

public sealed class AuthoritativeOverlayState
{
    private readonly Dictionary<long, OverlayMessage> _overlays = new();
    private readonly Dictionary<long, long> _latestRevision = new();
    private readonly OverlaySnapshotAssembler _snapshotAssembler = new();

    public long AppliedEpoch { get; private set; }
    public long AppliedVersion { get; private set; }

    public IReadOnlyList<OverlayMessage> Snapshot() =>
        _overlays.Values.OrderBy(o => o.OverlayId).ToArray();

    public bool TryGet(long overlayId, out OverlayMessage? overlay) =>
        _overlays.TryGetValue(overlayId, out overlay);

    public long LatestRevision(long overlayId) =>
        _latestRevision.TryGetValue(overlayId, out var revision) ? revision : 0;

    public void ResetPendingSnapshot() => _snapshotAssembler.Reset();

    public OverlayStateApplyResult Apply(OverlayWireMessage message)
    {
        return message.Type switch
        {
            OverlayMessageTypes.Diff => ApplyDiff(message),
            OverlayMessageTypes.Snapshot => ApplySnapshot(message),
            _ => new(OverlayStateApplyStatus.Ignored)
        };
    }

    private OverlayStateApplyResult ApplyDiff(OverlayWireMessage message)
    {
        if (!AcceptGlobalVersion(message, out var failure))
            return failure;

        var overlayId = message.OverlayId!.Value;
        var revision = message.Revision!.Value;
        var knownRevision = LatestRevision(overlayId);
        var changed = false;

        if (revision > knownRevision)
        {
            if (message.Operation == OverlayDiffOperations.Upsert)
            {
                _overlays[overlayId] = message.Overlay!;
                changed = true;
            }
            else
            {
                changed = _overlays.Remove(overlayId);
            }
            _latestRevision[overlayId] = revision;
        }

        Advance(message.StateEpoch!.Value, message.StateVersion!.Value);
        return new OverlayStateApplyResult(
            OverlayStateApplyStatus.Applied,
            changed);
    }

    private OverlayStateApplyResult ApplySnapshot(OverlayWireMessage message)
    {
        var result = _snapshotAssembler.Add(
            message,
            AppliedEpoch,
            AppliedVersion);

        return result.Status switch
        {
            OverlaySnapshotAssemblyStatus.Stale =>
                new(OverlayStateApplyStatus.StaleIgnored),
            OverlaySnapshotAssemblyStatus.Conflict =>
                new(OverlayStateApplyStatus.ResyncRequired),
            OverlaySnapshotAssemblyStatus.Incomplete =>
                new(OverlayStateApplyStatus.SnapshotIncomplete),
            OverlaySnapshotAssemblyStatus.Complete =>
                ApplyCompletedSnapshot(result),
            _ => new(OverlayStateApplyStatus.Ignored)
        };
    }

    private OverlayStateApplyResult ApplyCompletedSnapshot(
        OverlaySnapshotAssemblyResult result)
    {
        var next = result.Overlays!.ToDictionary(o => o.OverlayId);
        var changed = !Equivalent(_overlays, next);

        _overlays.Clear();
        _latestRevision.Clear();
        foreach (var overlay in next.Values)
        {
            _overlays[overlay.OverlayId] = overlay;
            _latestRevision[overlay.OverlayId] = overlay.Revision;
        }

        AppliedEpoch = result.StateEpoch!.Value;
        AppliedVersion = result.StateVersion!.Value;
        return new(
            OverlayStateApplyStatus.SnapshotApplied,
            changed);
    }

    private bool AcceptGlobalVersion(
        OverlayWireMessage message,
        out OverlayStateApplyResult failure)
    {
        failure = new(OverlayStateApplyStatus.Ignored);
        if (message.StateEpoch is null || message.StateVersion is null)
        {
            failure = new(OverlayStateApplyStatus.ResyncRequired);
            return false;
        }

        var epoch = message.StateEpoch.Value;
        var version = message.StateVersion.Value;
        if (epoch < AppliedEpoch ||
            (epoch == AppliedEpoch && version <= AppliedVersion))
        {
            failure = new(OverlayStateApplyStatus.StaleIgnored);
            return false;
        }

        if (AppliedEpoch == 0 ||
            epoch > AppliedEpoch ||
            version != AppliedVersion + 1)
        {
            failure = new(OverlayStateApplyStatus.ResyncRequired);
            return false;
        }

        return true;
    }

    private void Advance(long epoch, long version)
    {
        AppliedEpoch = epoch;
        AppliedVersion = version;
    }

    private static bool Equivalent(
        IReadOnlyDictionary<long, OverlayMessage> a,
        IReadOnlyDictionary<long, OverlayMessage> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var pair in a)
        {
            if (!b.TryGetValue(pair.Key, out var other)) return false;
            if (!Equivalent(pair.Value, other)) return false;
        }
        return true;
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

public sealed record OverlayCommandPreview(
    long OverlayId,
    long OwnerId,
    int BeaconId,
    string Kind,
    IReadOnlyList<SharedPointMessage> Geometry,
    long CreatedAt
);
