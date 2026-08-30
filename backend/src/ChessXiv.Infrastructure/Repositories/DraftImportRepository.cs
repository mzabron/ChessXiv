using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

        if (dbContext.Database.GetDbConnection() is not NpgsqlConnection connection)
        {
            await dbContext.StagingGames.AddRangeAsync(games, cancellationToken);
            return;
        }

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await PostgresBulkCopy.WriteStagingGamesAsync(connection, games, cancellationToken);
        await PostgresBulkCopy.WriteStagingPositionsAsync(connection, games, cancellationToken);
    }
}
