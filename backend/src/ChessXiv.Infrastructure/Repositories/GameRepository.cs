using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChessXiv.Infrastructure.Repositories;

public class GameRepository(ChessXivDbContext dbContext) : IGameRepository
{
    public async Task AddRangeAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default)
    {
        if (games.Count == 0)
        {
            return;
        }

        if (dbContext.Database.GetDbConnection() is not NpgsqlConnection connection)
        {
            // Non-PostgreSQL providers (in-memory tests) fall back to change tracking.
            await dbContext.Games.AddRangeAsync(games, cancellationToken);
            return;
        }

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await PostgresBulkCopy.WriteGamesAsync(connection, games, cancellationToken);
        await PostgresBulkCopy.WritePositionsAsync(connection, games, cancellationToken);
    }
}
