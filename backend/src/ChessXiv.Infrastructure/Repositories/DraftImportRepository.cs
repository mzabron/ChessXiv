using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Infrastructure.Repositories;

public sealed class DraftImportRepository(ChessXivDbContext dbContext) : IDraftImportRepository
{
    public async Task ClearStagingGamesAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        await dbContext.StagingGames
            .Where(g => g.OwnerUserId == ownerUserId)
            .ExecuteDeleteAsync(cancellationToken);
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
