using ChessXiv.Application.Contracts;
using System.Collections.Concurrent;

namespace ChessXiv.Api.Services;

public sealed class DraftImportProgressCache
{
    private readonly ConcurrentDictionary<string, DraftImportProgressUpdate> cache = new(StringComparer.Ordinal);

    public void Set(string userId, DraftImportProgressUpdate update)
    {
        cache[userId] = update;
    }

    public DraftImportProgressUpdate? Get(string userId)
    {
        return cache.TryGetValue(userId, out var update) ? update : null;
    }
}