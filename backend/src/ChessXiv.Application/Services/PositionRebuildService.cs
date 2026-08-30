using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Services;

public sealed class PositionRebuildService(
    IPgnParser pgnParser,
    IPositionImportCoordinator positionImportCoordinator,
    IGameSourceRepository gameSourceRepository,
    IPositionRebuildRepository positionRebuildRepository) : IPositionRebuildService
{
    public async Task<int> RebuildAsync(
        int batchSize = 500,
        IProgress<PositionRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        var totalGames = await gameSourceRepository.CountAsync(cancellationToken);
        var processed = 0;
        var rebuilt = 0;
        var batch = new List<ParsedGame>(batchSize);

        await foreach (var stored in gameSourceRepository.StreamAsync(batchSize, cancellationToken))
        {
            // The stored PGN is reparsed rather than trusted for metadata: only the moves
            // are needed here, and the game row keeps its identity and existing columns.
            var parsed = pgnParser.ParsePgn(stored.Pgn).FirstOrDefault();
            processed++;

            if (parsed is null)
            {
                continue;
            }

            parsed.Game.Id = stored.Id;
            parsed.Game.Result = stored.Result;
            batch.Add(parsed);

            if (batch.Count >= batchSize)
            {
                rebuilt += await FlushAsync(batch, cancellationToken);
                batch.Clear();
                progress?.Report(new PositionRebuildProgress(processed, totalGames));
            }
        }

        if (batch.Count > 0)
        {
            rebuilt += await FlushAsync(batch, cancellationToken);
        }

        progress?.Report(new PositionRebuildProgress(processed, totalGames));
        return rebuilt;
    }

    private async Task<int> FlushAsync(IReadOnlyCollection<ParsedGame> batch, CancellationToken cancellationToken)
    {
        await positionImportCoordinator.PopulateAsync(batch, cancellationToken);
        await positionRebuildRepository.ReplacePositionsAsync(
            batch.Select(b => b.Game).ToArray(),
            cancellationToken);

        return batch.Count;
    }
}
