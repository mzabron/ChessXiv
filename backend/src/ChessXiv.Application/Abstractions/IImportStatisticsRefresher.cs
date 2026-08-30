namespace ChessXiv.Application.Abstractions;

/// <summary>
/// Refreshes planner statistics for the tables a bulk import writes to.
///
/// A binary COPY of millions of rows leaves the statistics for Games, Positions and
/// UserDatabaseGames badly stale, and autovacuum may not catch up for a long time. Until it
/// does, the opening-tree query can lose its index-only scan on IX_Positions_PosKey - which
/// is precisely the plan the whole position-storage design exists to get.
/// </summary>
public interface IImportStatisticsRefresher
{
    Task RefreshAfterDatabaseImportAsync(CancellationToken cancellationToken = default);

    Task RefreshAfterDraftImportAsync(CancellationToken cancellationToken = default);
}
