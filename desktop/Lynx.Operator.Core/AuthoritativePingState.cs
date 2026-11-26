using Lynx.Protocol;

namespace Lynx.Operator.Core;

public enum StateApplyStatus
{
    Applied,
    SnapshotIncomplete,
    SnapshotApplied,
    StaleIgnored,
    ResyncRequired,
    Ignored
}

public sealed record StateApplyResult(
    StateApplyStatus Status,
    bool StateChanged = false
);

public sealed class AuthoritativePingState
{
    private readonly Dictionary<long, PingMessage> _pings = new();
    private readonly SnapshotAssembler _snapshotAssembler = new();

    public long AppliedEpoch { get; private set; }
    public long AppliedVersion { get; private set; }

    public IReadOnlyList<PingMessage> Snapshot() =>
        _pings.Values.OrderBy(ping => ping.Id).ToArray();

    public bool TryGet(long pingId, out PingMessage? ping) =>
        _pings.TryGetValue(pingId, out ping);

    public StateApplyResult Apply(LynxMessage message)
    {
        return message.Type switch
        {
            LynxMessageTypes.PingCreate => ApplyCreate(message),
            LynxMessageTypes.PingRemove => ApplyRemove(message),
            LynxMessageTypes.Snapshot => ApplySnapshot(message),
            _ => new StateApplyResult(StateApplyStatus.Ignored)
        };
    }

    public void ResetPendingSnapshot() => _snapshotAssembler.Reset();

    private StateApplyResult ApplyCreate(LynxMessage message)
    {
        if (!TryAcceptMutationVersion(message, out var versionResult))
            return versionResult;

        var ping = message.Ping!;
        var changed = !_pings.TryGetValue(ping.Id, out var existing) || existing != ping;
        _pings[ping.Id] = ping;
        Advance(message);
        return new StateApplyResult(StateApplyStatus.Applied, changed);
    }

    private StateApplyResult ApplyRemove(LynxMessage message)
    {
        if (!TryAcceptMutationVersion(message, out var versionResult))
            return versionResult;

        var changed = _pings.Remove(message.PingId!.Value);
        Advance(message);
        return new StateApplyResult(StateApplyStatus.Applied, changed);
    }

    private StateApplyResult ApplySnapshot(LynxMessage message)
    {
        var result = _snapshotAssembler.Add(
            message,
            AppliedEpoch,
            AppliedVersion);

        return result.Status switch
        {
            SnapshotAssemblyStatus.Stale =>
                new StateApplyResult(StateApplyStatus.StaleIgnored),

            SnapshotAssemblyStatus.Conflict =>
                new StateApplyResult(StateApplyStatus.ResyncRequired),

            SnapshotAssemblyStatus.Incomplete =>
                new StateApplyResult(StateApplyStatus.SnapshotIncomplete),

            SnapshotAssemblyStatus.Complete =>
                ApplyCompletedSnapshot(result),

            _ => new StateApplyResult(StateApplyStatus.Ignored)
        };
    }

    private StateApplyResult ApplyCompletedSnapshot(
        SnapshotAssemblyResult result)
    {
        var next = result.Pings!.ToDictionary(ping => ping.Id);
        var changed = !Equivalent(_pings, next);

        _pings.Clear();
        foreach (var pair in next)
            _pings[pair.Key] = pair.Value;

        AppliedEpoch = result.StateEpoch!.Value;
        AppliedVersion = result.StateVersion!.Value;

        return new StateApplyResult(
            StateApplyStatus.SnapshotApplied,
            changed);
    }

    private bool TryAcceptMutationVersion(
        LynxMessage message,
        out StateApplyResult failure)
    {
        failure = new StateApplyResult(StateApplyStatus.Ignored);

        if (message.StateEpoch is null || message.StateVersion is null)
        {
            failure = new StateApplyResult(StateApplyStatus.ResyncRequired);
            return false;
        }

        var epoch = message.StateEpoch.Value;
        var version = message.StateVersion.Value;

        if (epoch < AppliedEpoch ||
            (epoch == AppliedEpoch && version <= AppliedVersion))
        {
            failure = new StateApplyResult(StateApplyStatus.StaleIgnored);
            return false;
        }

        if (AppliedEpoch == 0 ||
            epoch > AppliedEpoch ||
            version != AppliedVersion + 1)
        {
            failure = new StateApplyResult(StateApplyStatus.ResyncRequired);
            return false;
        }

        return true;
    }

    private void Advance(LynxMessage message)
    {
        AppliedEpoch = message.StateEpoch!.Value;
        AppliedVersion = message.StateVersion!.Value;
    }

    private static bool Equivalent(
        IReadOnlyDictionary<long, PingMessage> a,
        IReadOnlyDictionary<long, PingMessage> b)
    {
        if (a.Count != b.Count) return false;

        foreach (var pair in a)
        {
            if (!b.TryGetValue(pair.Key, out var other) || pair.Value != other)
                return false;
        }

        return true;
    }
}
