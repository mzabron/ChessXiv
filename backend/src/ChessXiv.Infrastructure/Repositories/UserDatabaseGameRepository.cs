using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChessXiv.Infrastructure.Repositories;

public sealed class UserDatabaseGameRepository(ChessXivDbContext dbContext) : IUserDatabaseGameRepository
{
    public async Task AddRangeAsync(IReadOnlyCollection<UserDatabaseGame> userDatabaseGames, CancellationToken cancellationToken = default)
    {
        if (userDatabaseGames.Count == 0)
        {
            return;
        }

        if (dbContext.Database.GetDbConnection() is not NpgsqlConnection connection)
        {
            await dbContext.UserDatabaseGames.AddRangeAsync(userDatabaseGames, cancellationToken);
            return;
        }

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await PostgresBulkCopy.WriteUserDatabaseGamesAsync(connection, userDatabaseGames, cancellationToken);
    }
}
