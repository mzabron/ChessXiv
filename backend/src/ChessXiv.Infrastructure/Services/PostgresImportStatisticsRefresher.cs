using ChessXiv.Application.Abstractions;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Infrastructure.Services;

/// <summary>
/// The ANALYZE used to be written inline in the API's background import worker, so the CLI
/// - the path that imports the largest files of all - silently skipped it. Owning it here
/// means both callers get the same treatment.
/// </summary>
public sealed class PostgresImportStatisticsRefresher(ChessXivDbContext dbContext) : IImportStatisticsRefresher
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    public Task RefreshAfterDatabaseImportAsync(CancellationToken cancellationToken = default) =>
        AnalyzeAsync("ANALYZE \"Games\", \"Positions\", \"UserDatabaseGames\";", cancellationToken);

    public Task RefreshAfterDraftImportAsync(CancellationToken cancellationToken = default) =>
        AnalyzeAsync("ANALYZE \"StagingGames\", \"StagingPositions\";", cancellationToken);

    private async Task AnalyzeAsync(string sql, CancellationToken cancellationToken)
    {
        // Non-Postgres providers (the in-memory ones used by some tests) have no ANALYZE.
        if (dbContext.Database.ProviderName != NpgsqlProviderName)
        {
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
