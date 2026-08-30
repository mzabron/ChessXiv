using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Services;

/// <summary>
/// The single non-staging import path: parses a PGN straight into a user's database.
/// Used by the web upload and by the CLI importer.
/// </summary>
public sealed class DirectDatabaseImportService(
    IPgnParser pgnParser,
    IPositionImportCoordinator positionImportCoordinator,
    IGameRepository gameRepository,
    IUserDatabaseGameRepository userDatabaseGameRepository,
    IDraftPromotionRepository draftPromotionRepository,
    IUnitOfWork unitOfWork) : IDirectDatabaseImportService
{
    public async Task<DraftImportResult> ImportToDatabaseAsync(
        TextReader reader,
        string ownerUserId,
        Guid userDatabaseId,
        int batchSize = 500,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        var userDatabase = await draftPromotionRepository.GetUserDatabaseAsync(userDatabaseId, cancellationToken);
        if (userDatabase is null)
        {
            throw new InvalidOperationException("Target user database was not found.");
        }

        if (!string.Equals(userDatabase.OwnerUserId, ownerUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Target user database does not belong to the current user.");
        }

        var parsedCount = 0;
        var importedCount = 0;
        var skippedCount = 0;
        var batch = new List<ParsedGame>(batchSize);

        // Each batch commits on its own. A single transaction spanning a 100 MB PGN would
        // hold locks and block autovacuum for minutes, and lose the whole import on any
        // failure; per-batch commits keep the transaction short and the progress durable.
        await foreach (var parsedGame in pgnParser.ParsePgnAsync(reader, cancellationToken))
        {
            parsedCount++;
            if (string.IsNullOrWhiteSpace(parsedGame.Game.White) || string.IsNullOrWhiteSpace(parsedGame.Game.Black))
            {
                skippedCount++;
                continue;
            }

            batch.Add(parsedGame);
            importedCount++;

            if (batch.Count >= batchSize)
            {
                await PersistBatchAsync(batch, userDatabaseId, cancellationToken);
                batch.Clear();
                progress?.Report(new ImportProgress(parsedCount, importedCount, skippedCount));
            }
        }

        if (batch.Count > 0)
        {
            await PersistBatchAsync(batch, userDatabaseId, cancellationToken);
            progress?.Report(new ImportProgress(parsedCount, importedCount, skippedCount));
        }

        await draftPromotionRepository.SyncGameCountAsync(userDatabaseId, cancellationToken);

        return new DraftImportResult(parsedCount, importedCount, skippedCount);
    }

    private async Task PersistBatchAsync(
        IReadOnlyCollection<ParsedGame> parsedGames,
        Guid userDatabaseId,
        CancellationToken cancellationToken)
    {
        var addedAtUtc = DateTime.UtcNow;
        var games = parsedGames.Select(ParsedGameFinalizer.Finalize).ToArray();

        await positionImportCoordinator.PopulateAsync(parsedGames, cancellationToken);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await gameRepository.AddRangeAsync(games, cancellationToken);

            var links = games.Select(g => new UserDatabaseGame
            {
                UserDatabaseId = userDatabaseId,
                GameId = g.Id,
                AddedAtUtc = addedAtUtc,
                Date = g.Date,
                Year = g.Year <= 0 ? null : g.Year,
                Event = g.Event,
                Round = g.Round,
                Site = g.Site
            }).ToArray();

            await userDatabaseGameRepository.AddRangeAsync(links, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            unitOfWork.ClearTracker();
        }
    }
}
