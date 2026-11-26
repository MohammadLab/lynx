namespace Lynx.Protocol;

public enum OverlaySnapshotAssemblyStatus
{
    Incomplete,
    Complete,
    Stale,
    Conflict
}

public sealed record OverlaySnapshotAssemblyResult(
    OverlaySnapshotAssemblyStatus Status,
    long? StateEpoch = null,
    long? StateVersion = null,
    IReadOnlyList<OverlayMessage>? Overlays = null
);

public sealed class OverlaySnapshotAssembler
{
    private long? _snapshotId;
    private long _stateEpoch;
    private long _stateVersion;
    private int _chunkCount;
    private readonly Dictionary<int, IReadOnlyList<OverlayMessage>> _chunks = new();

    public OverlaySnapshotAssemblyResult Add(
        OverlayWireMessage message,
        long minimumStateEpoch,
        long minimumStateVersion)
    {
        if (message.Type != OverlayMessageTypes.Snapshot ||
            message.SnapshotId is null ||
            message.StateEpoch is null ||
            message.StateVersion is null ||
            message.ChunkIndex is null ||
            message.ChunkCount is null ||
            message.Overlays is null)
        {
            throw new ArgumentException("Message is not a complete OVERLAY_SNAPSHOT chunk.", nameof(message));
        }

        var snapshotId = message.SnapshotId.Value;
        var epoch = message.StateEpoch.Value;
        var version = message.StateVersion.Value;
        var chunkIndex = message.ChunkIndex.Value;
        var chunkCount = message.ChunkCount.Value;

        if (epoch < minimumStateEpoch ||
            (epoch == minimumStateEpoch && version < minimumStateVersion))
        {
            return new(
                OverlaySnapshotAssemblyStatus.Stale,
                epoch,
                version);
        }

        if (_snapshotId is null ||
            epoch > _stateEpoch ||
            (epoch == _stateEpoch && version > _stateVersion) ||
            (epoch == _stateEpoch && version == _stateVersion && snapshotId > _snapshotId.Value))
        {
            Start(snapshotId, epoch, version, chunkCount);
        }
        else if (epoch != _stateEpoch ||
                 version != _stateVersion ||
                 snapshotId != _snapshotId)
        {
            return new(
                OverlaySnapshotAssemblyStatus.Stale,
                epoch,
                version);
        }

        if (chunkCount != _chunkCount)
            return new(OverlaySnapshotAssemblyStatus.Conflict, epoch, version);

        if (_chunks.TryGetValue(chunkIndex, out var existing))
        {
            if (!existing.SequenceEqual(message.Overlays))
                return new(OverlaySnapshotAssemblyStatus.Conflict, epoch, version);
        }
        else
        {
            _chunks[chunkIndex] = message.Overlays;
        }

        if (_chunks.Count != _chunkCount)
            return new(OverlaySnapshotAssemblyStatus.Incomplete, epoch, version);

        var combined = Enumerable.Range(0, _chunkCount)
            .SelectMany(index => _chunks[index])
            .ToArray();

        var completedEpoch = _stateEpoch;
        var completedVersion = _stateVersion;
        Reset();
        return new(
            OverlaySnapshotAssemblyStatus.Complete,
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

    private void Start(long snapshotId, long epoch, long version, int chunkCount)
    {
        _snapshotId = snapshotId;
        _stateEpoch = epoch;
        _stateVersion = version;
        _chunkCount = chunkCount;
        _chunks.Clear();
    }
}
