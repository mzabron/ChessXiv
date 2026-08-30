namespace ChessXiv.Application.Abstractions;

public interface IPositionRebuildService
{
    /// <summary>
    /// Regenerates the Positions table from the PGNs already stored in Games. Used after the
    /// storage-format change, and as a repair tool if positions ever drift from the games.
    /// </summary>
    Task<int> RebuildAsync(
        int batchSize = 500,
        IProgress<PositionRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record PositionRebuildProgress(int GamesProcessed, int TotalGames);
