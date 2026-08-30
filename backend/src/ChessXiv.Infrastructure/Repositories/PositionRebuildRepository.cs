using System.Runtime.CompilerServices;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ChessXiv.Infrastructure.Repositories;

public sealed class GameSourceRepository(ChessXivDbContext dbContext) : IGameSourceRepository
{
    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Games.AsNoTracking().CountAsync(cancellationToken);
    }

    public async IAsyncEnumerable<StoredGamePgn> StreamAsync(
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Keyset pagination rather than Skip/Take: offsets get quadratically slower as the
        // rebuild walks a multi-hundred-thousand row table.
        var lastId = Guid.Empty;

        while (true)
        {
            var page = await dbContext.Games
                .AsNoTracking()
                .Where(g => g.Id.CompareTo(lastId) > 0)
                .OrderBy(g => g.Id)
                .Take(batchSize)
                .Select(g => new StoredGamePgn(g.Id, g.Pgn, g.Result))
                .ToListAsync(cancellationToken);

            if (page.Count == 0)
            {
                yield break;
            }

            foreach (var game in page)
            {
                yield return game;
            }

            lastId = page[^1].Id;
        }
    }
}

public sealed class PositionRebuildRepository(ChessXivDbContext dbContext) : IPositionRebuildRepository
{
    public async Task ReplacePositionsAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default)
    {
        if (games.Count == 0)
        {
            return;
        }

        var gameIds = games.Select(g => g.Id).ToArray();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await dbContext.Positions
                .Where(p => gameIds.Contains(p.GameId))
                .ExecuteDeleteAsync(cancellationToken);

            var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await PostgresBulkCopy.WritePositionsAsync(connection, games, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
