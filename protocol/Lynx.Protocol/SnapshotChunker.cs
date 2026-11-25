namespace Lynx.Protocol;

public static class SnapshotChunker
{
    public static IReadOnlyList<IReadOnlyList<PingMessage>> ChunkBySerializedBudget(
        IReadOnlyList<PingMessage> pings,
        long snapshotId,
        long stateEpoch,
        long stateVersion,
        int maxPayloadBytes)
    {
        if (maxPayloadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

        if (pings.Count == 0)
            return new[] { (IReadOnlyList<PingMessage>)Array.Empty<PingMessage>() };

        var chunks = new List<IReadOnlyList<PingMessage>>();
        var current = new List<PingMessage>();

        foreach (var ping in pings)
        {
            current.Add(ping);

            // Use worst-case chunk metadata widths while budgeting so the final
            // serialized chunk cannot grow past the tested size because of index/count digits.
            var candidate = LynxMessageCodec.SnapshotChunk(
                snapshotId,
                stateEpoch,
                stateVersion,
                int.MaxValue,
                int.MaxValue,
                current);

            var size = LynxMessageCodec.Serialize(candidate).Length;
            if (size <= maxPayloadBytes)
                continue;

            current.RemoveAt(current.Count - 1);
            if (current.Count == 0)
                throw new InvalidOperationException(
                    "A single snapshot item exceeds the configured UDP payload budget.");

            chunks.Add(current.ToArray());
            current.Clear();
            current.Add(ping);

            var single = LynxMessageCodec.SnapshotChunk(
                snapshotId,
                stateEpoch,
                stateVersion,
                int.MaxValue,
                int.MaxValue,
                current);

            if (LynxMessageCodec.Serialize(single).Length > maxPayloadBytes)
                throw new InvalidOperationException(
                    "A single snapshot item exceeds the configured UDP payload budget.");
        }

        if (current.Count > 0)
            chunks.Add(current.ToArray());

        return chunks;
    }
}