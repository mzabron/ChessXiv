using System.Collections.Concurrent;

namespace ChessXiv.Api.Services;

public sealed class ImportProgressConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> byUserId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> byConnectionId = new(StringComparer.Ordinal);

    public void Add(string userId, string connectionId)
    {
        var userConnections = byUserId.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        userConnections[connectionId] = 0;
        byConnectionId[connectionId] = userId;
    }

    public void Remove(string connectionId)
    {
        if (!byConnectionId.TryRemove(connectionId, out var userId))
        {
            return;
        }

        if (!byUserId.TryGetValue(userId, out var userConnections))
        {
            return;
        }

        userConnections.TryRemove(connectionId, out _);

        if (userConnections.IsEmpty)
        {
            byUserId.TryRemove(userId, out _);
        }
    }

    public IReadOnlyCollection<string> GetConnectionIds(string userId)
    {
        if (!byUserId.TryGetValue(userId, out var userConnections))
        {
            return Array.Empty<string>();
        }

        return userConnections.Keys.ToArray();
    }
}