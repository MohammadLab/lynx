namespace Lynx.Protocol;

public enum SnapshotAssemblyStatus
{
    Incomplete,
    Complete,
    Stale,
    Conflict,
}

public sealed record SnapshotAssemblyResult(
    SnapshotAssemblyStatus Status,
    long? StateEpoch = null,
    long? StateVersion = null,
    IReadOnlyList<PingMessage>? Pings = null
);

public sealed class SnapshotAssembler
{
    private long? _snapshotId;
    private long _stateEpoch;
    private long _stateVersion;
    private int _chunkCount;
    private readonly Dictionary<int, IReadOnlyList<PingMessage>> _chunks = new();

    public SnapshotAssemblyResult Add(
        LynxMessage message,
        long minimumStateEpoch,
        long minimumStateVersion)
    {
        if (message.Type != LynxMessageTypes.Snapshot ||
            message.SnapshotId is null ||
            message.StateEpoch is null ||
            message.StateVersion is null ||
            message.ChunkIndex is null ||
            message.ChunkCount is null ||
            message.Pings is null)
        {
            throw new ArgumentException("Message is not a complete SNAPSHOT chunk.", nameof(message));
        }

        var snapshotId = message.SnapshotId.Value;
        var stateEpoch = message.StateEpoch.Value;
        var stateVersion = message.StateVersion.Value;
        var chunkIndex = message.ChunkIndex.Value;
        var chunkCount = message.ChunkCount.Value;

        if (stateEpoch < minimumStateEpoch ||
            (stateEpoch == minimumStateEpoch && stateVersion < minimumStateVersion))
        {
            return new(
                SnapshotAssemblyStatus.Stale,
                stateEpoch,
                stateVersion);
        }

        if (_snapshotId is null ||
            stateEpoch > _stateEpoch ||
            (stateEpoch == _stateEpoch && stateVersion > _stateVersion) ||
            (stateEpoch == _stateEpoch &&
             stateVersion == _stateVersion &&
             snapshotId > _snapshotId.Value))
        {
            Start(snapshotId, stateEpoch, stateVersion, chunkCount);
        }
        else if (stateEpoch != _stateEpoch ||
                 snapshotId != _snapshotId ||
                 stateVersion != _stateVersion)
        {
            return new(
                SnapshotAssemblyStatus.Stale,
                stateEpoch,
                stateVersion);
        }

        if (chunkCount != _chunkCount)
            return new(SnapshotAssemblyStatus.Conflict, stateEpoch, stateVersion);

        if (_chunks.TryGetValue(chunkIndex, out var existing))
        {
            if (!existing.SequenceEqual(message.Pings))
                return new(SnapshotAssemblyStatus.Conflict, stateEpoch, stateVersion);
        }
        else
        {
            _chunks[chunkIndex] = message.Pings;
        }

        if (_chunks.Count != _chunkCount)
            return new(SnapshotAssemblyStatus.Incomplete, stateEpoch, stateVersion);

        var combined = Enumerable.Range(0, _chunkCount)
            .SelectMany(index => _chunks[index])
            .ToArray();

        var completedEpoch = _stateEpoch;
        var completedVersion = _stateVersion;
        Reset();
        return new(
            SnapshotAssemblyStatus.Complete,
            completedEpoch,
            completedVersion,
            combined);
    }

    public void Reset()
    {
        _snapshotId = null;
        _stateEpoch = 0;
        _stateVersion = 0;
        _chunkCount = 0;
        _chunks.Clear();
    }

    private void Start(
        long snapshotId,
        long stateEpoch,
        long stateVersion,
        int chunkCount)
    {
        _snapshotId = snapshotId;
        _stateEpoch = stateEpoch;
        _stateVersion = stateVersion;
        _chunkCount = chunkCount;
        _chunks.Clear();
    }
}