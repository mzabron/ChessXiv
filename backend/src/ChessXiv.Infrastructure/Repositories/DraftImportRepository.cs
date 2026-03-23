using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Infrastructure.Repositories;

public sealed class DraftImportRepository(ChessXivDbContext dbContext) : IDraftImportRepository
{
    public Task<StagingImportSession?> GetImportSessionAsync(Guid importSessionId, string ownerUserId, CancellationToken cancellationToken = default)
    {
        return dbContext.StagingImportSessions
            .FirstOrDefaultAsync(s => s.Id == importSessionId && s.OwnerUserId == ownerUserId, cancellationToken);
    }

    public Task<int> CountStagingGamesAsync(Guid importSessionId, string ownerUserId, CancellationToken cancellationToken = default)
    {
        return dbContext.StagingGames
            .CountAsync(g => g.ImportSessionId == importSessionId && g.OwnerUserId == ownerUserId, cancellationToken);
    }

    public Task DeleteUnpromotedSessionsByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        return dbContext.StagingImportSessions
            .Where(s => s.OwnerUserId == ownerUserId && !s.PromotedAtUtc.HasValue)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task AddImportSessionAsync(StagingImportSession session, CancellationToken cancellationToken = default)
    {
        await dbContext.StagingImportSessions.AddAsync(session, cancellationToken);
    }

    public async Task AddStagingGamesAsync(IReadOnlyCollection<StagingGame> games, CancellationToken cancellationToken = default)
    {
        if (games.Count == 0)
        {
            return;
        }

        await dbContext.StagingGames.AddRangeAsync(games, cancellationToken);
    }
}
