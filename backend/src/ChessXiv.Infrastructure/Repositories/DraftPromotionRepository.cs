using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Infrastructure.Repositories;

public sealed class DraftPromotionRepository(ChessXivDbContext dbContext) : IDraftPromotionRepository
{
    public Task<UserDatabase?> GetUserDatabaseAsync(Guid userDatabaseId, CancellationToken cancellationToken = default)
    {
        return dbContext.UserDatabases
            .FirstOrDefaultAsync(d => d.Id == userDatabaseId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<StagingGame>> GetStagingGamesPageAsync(
        string ownerUserId,
        int take,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.StagingGames
            .AsNoTracking()
            .Where(g => g.OwnerUserId == ownerUserId)
            .OrderBy(g => g.Id)
            .Take(take)
            .Include(g => g.Moves)
            .Include(g => g.Positions)
            .ToListAsync(cancellationToken);
    }

    public async Task AddGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        await dbContext.Games.AddAsync(game, cancellationToken);
    }

    public async Task AddUserDatabaseGameAsync(UserDatabaseGame userDatabaseGame, CancellationToken cancellationToken = default)
    {
        await dbContext.UserDatabaseGames.AddAsync(userDatabaseGame, cancellationToken);
    }

    public async Task RemoveStagingGamesAsync(IReadOnlyCollection<Guid> stagingGameIds, CancellationToken cancellationToken = default)
    {
        if (stagingGameIds.Count == 0)
        {
            return;
        }

        var gamesToRemove = await dbContext.StagingGames
            .Where(g => stagingGameIds.Contains(g.Id))
            .ToListAsync(cancellationToken);

        if (gamesToRemove.Count == 0)
        {
            return;
        }

        dbContext.StagingGames.RemoveRange(gamesToRemove);
    }

}
