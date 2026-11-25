using System.Net;

namespace Lynx.Relay.Core;

public sealed record RegisteredClient(
    long ClientId,
    IPEndPoint Endpoint,
    DateTimeOffset LastSeenUtc
);

public sealed class ClientRegistry
{
    private readonly Dictionary<long, RegisteredClient> _clients = new();

    public int Count => _clients.Count;

    public IReadOnlyCollection<RegisteredClient> Clients => _clients.Values.ToArray();

    public void RegisterOrRefresh(
        long clientId,
        IPEndPoint endpoint,
        DateTimeOffset nowUtc)
    {
        _clients[clientId] = new RegisteredClient(
            clientId,
            endpoint,
            nowUtc);
    }

    public bool TryGet(long clientId, out RegisteredClient? client) =>
        _clients.TryGetValue(clientId, out client);

    public int RemoveExpired(DateTimeOffset nowUtc, TimeSpan timeout)
    {
        var expired = _clients
            .Where(pair => nowUtc - pair.Value.LastSeenUtc > timeout)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var clientId in expired)
            _clients.Remove(clientId);

        return expired.Length;
    }
}