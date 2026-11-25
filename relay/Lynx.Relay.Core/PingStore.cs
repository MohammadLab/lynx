using Lynx.Protocol;

namespace Lynx.Relay.Core;

public enum PingAddResult
{
    Added,
    Duplicate,
    Conflict
}

public enum PingRemoveResult
{
    Removed,
    NotFound,
    SenderMismatch
}

public sealed class PingStore
{
    private readonly Dictionary<long, PingMessage> _pings = new();

    public int Count => _pings.Count;

    public IReadOnlyList<PingMessage> Snapshot() =>
        _pings.Values.OrderBy(ping => ping.Id).ToArray();

    public PingAddResult TryAdd(PingMessage ping)
    {
        if (!_pings.TryGetValue(ping.Id, out var existing))
        {
            _pings.Add(ping.Id, ping);
            return PingAddResult.Added;
        }

        return existing == ping
            ? PingAddResult.Duplicate
            : PingAddResult.Conflict;
    }

    public PingRemoveResult TryRemove(long pingId, long senderId)
    {
        if (!_pings.TryGetValue(pingId, out var existing))
            return PingRemoveResult.NotFound;

        if (existing.SenderId != senderId)
            return PingRemoveResult.SenderMismatch;

        _pings.Remove(pingId);
        return PingRemoveResult.Removed;
    }

    public bool TryGet(long id, out PingMessage? ping) =>
        _pings.TryGetValue(id, out ping);
}
