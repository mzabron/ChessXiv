using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;

namespace ChessXiv.Infrastructure.Repositories;

public class GameRepository(ChessXivDbContext dbContext) : IGameRepository
{
    public async Task AddRangeAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default)
    {
        if (games.Count == 0)
        {
            return;
        }

        await dbContext.Games.AddRangeAsync(games, cancellationToken);
    }
}
