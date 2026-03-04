using ChessBase.Application.Abstractions.Repositories;
using ChessBase.Domain.Entities;
using ChessBase.Infrastructure.Data;

namespace ChessBase.Infrastructure.Repositories;

public class GameRepository(ChessBaseDbContext dbContext) : IGameRepository
{
    public async Task AddRangeAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default)
    {
        if (games.Count == 0)
        {
            return;
        }

        foreach (var game in games)
        {
            foreach (var move in game.Moves)
            {
                move.GameId = game.Id;
                move.Game = game;
            }
        }

        await dbContext.Games.AddRangeAsync(games, cancellationToken);
    }
}
