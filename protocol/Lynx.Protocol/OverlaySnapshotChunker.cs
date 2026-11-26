namespace Lynx.Protocol;

public static class OverlaySnapshotChunker
{
    public static IReadOnlyList<IReadOnlyList<OverlayMessage>> ChunkBySerializedBudget(
        IReadOnlyList<OverlayMessage> overlays,
        long snapshotId,
        long stateEpoch,
        long stateVersion,
        int maxPayloadBytes)
    {
        if (maxPayloadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

        if (overlays.Count == 0)
            return new[] { (IReadOnlyList<OverlayMessage>)Array.Empty<OverlayMessage>() };

        var chunks = new List<IReadOnlyList<OverlayMessage>>();
        var current = new List<OverlayMessage>();

        foreach (var overlay in overlays)
        {
            current.Add(overlay);
            if (Fits(current))
                continue;

            current.RemoveAt(current.Count - 1);
            if (current.Count == 0)
                throw new InvalidOperationException(
                    $"Overlay {overlay.OverlayId} exceeds the configured UDP snapshot budget.");

            chunks.Add(current.ToArray());
            current.Clear();
            current.Add(overlay);

            if (!Fits(current))
                throw new InvalidOperationException(
                    $"Overlay {overlay.OverlayId} exceeds the configured UDP snapshot budget.");
        }

        if (current.Count > 0)
            chunks.Add(current.ToArray());

        return chunks;

        bool Fits(IReadOnlyList<OverlayMessage> candidate)
        {
            var message = OverlayWireCodec.SnapshotChunk(
                snapshotId,
                stateEpoch,
                stateVersion,
                int.MaxValue,
                int.MaxValue,
                candidate);
            return OverlayWireCodec.Serialize(message).Length <= maxPayloadBytes;
        }
    }
}
