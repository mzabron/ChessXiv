using ChessXiv.Application.Abstractions;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Infrastructure.Services;

/// <summary>
/// The maintenance a bulk import owes the tables it just wrote to.
/// </summary>
/// <remarks>
/// This used to be written inline in the API's background import worker, so the CLI - the
/// path that imports the largest files of all - silently skipped it. Owning it here means
/// both callers get the same treatment.
/// </remarks>
public sealed class PostgresImportStatisticsRefresher(ChessXivDbContext dbContext) : IImportStatisticsRefresher
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    // VACUUM, not just ANALYZE. ANALYZE refreshes planner statistics; only VACUUM sets the
    // visibility map, and without that map an index-only scan cannot prove a row is visible
    // and falls back to reading the table for it. Freshly bulk-loaded rows always start
    // with the map unset, so the opening-tree query - the one the whole PosKey/NextMove
    // design exists to make fast - came out of an import doing hundreds of thousands of
    // heap fetches. Measured on a 1.6M-game database: 148,973 heap fetches and 5.3s before
    // a manual VACUUM, 3 fetches and under a second after it.
    //
    // VACUUM is slower than ANALYZE (minutes rather than seconds on a large table), which
    // is the right trade: it runs once per import and every opening-tree query afterwards
    // depends on it. It cannot run inside a transaction, so both callers invoke this
    // outside of one.
    public Task RefreshAfterDatabaseImportAsync(CancellationToken cancellationToken = default) =>
        RunAsync("VACUUM (ANALYZE) \"Games\", \"Positions\", \"UserDatabaseGames\";", cancellationToken);

    public Task RefreshAfterDraftImportAsync(CancellationToken cancellationToken = default) =>
        RunAsync("VACUUM (ANALYZE) \"StagingGames\", \"StagingPositions\";", cancellationToken);

    private async Task RunAsync(string sql, CancellationToken cancellationToken)
    {
        // Non-Postgres providers (the in-memory ones used by some tests) have no VACUUM.
        if (dbContext.Database.ProviderName != NpgsqlProviderName)
        {
            return;
        }

        // A large VACUUM runs far past the default 30-second command timeout.
        var previousTimeout = dbContext.Database.GetCommandTimeout();
        dbContext.Database.SetCommandTimeout((int)TimeSpan.FromHours(2).TotalSeconds);

        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        finally
        {
            dbContext.Database.SetCommandTimeout(previousTimeout);
        }
    }
}
