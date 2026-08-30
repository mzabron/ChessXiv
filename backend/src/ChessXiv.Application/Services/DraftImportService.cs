using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Entities;
using System.Diagnostics;

namespace ChessXiv.Application.Services;

public sealed class DraftImportService(
    IPgnParser pgnParser,
    IPositionImportCoordinator positionImportCoordinator,
    IDraftImportRepository draftImportRepository,
    IDraftSessionTracker draftSessionTracker,
    IUnitOfWork unitOfWork,
    IDraftImportProgressPublisher? progressPublisher = null) : IDraftImportService
{
    public async Task<DraftImportResult> ImportAsync(
        TextReader reader,
        string ownerUserId,
        int batchSize = 200,
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

        var now = DateTime.UtcNow;

        var parsedCount = 0;
        var importedCount = 0;
        var skippedCount = 0;
        var batch = new List<ParsedGame>(batchSize);
        var progressStopwatch = Stopwatch.StartNew();
        var lastProgressPublishMs = 0L;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await draftImportRepository.ClearStagingGamesAsync(ownerUserId, cancellationToken);
            await draftSessionTracker.TouchAsync(ownerUserId, cancellationToken);
            await PublishProgressAsync(ownerUserId, parsedCount, importedCount, skippedCount, isCompleted: false, isFailed: false, message: "Import started.", cancellationToken);

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
                    await PersistBatchAsync(batch, ownerUserId, now, cancellationToken);
                    batch.Clear();
                    var forcePublishMs = progressStopwatch.ElapsedMilliseconds;
                    lastProgressPublishMs = forcePublishMs;
                    await PublishProgressAsync(ownerUserId, parsedCount, importedCount, skippedCount, isCompleted: false, isFailed: false, message: null, cancellationToken);
                }
                else
                {
                    var nowMs = progressStopwatch.ElapsedMilliseconds;
                    if (nowMs - lastProgressPublishMs >= 500)
                    {
                        lastProgressPublishMs = nowMs;
                        await PublishProgressAsync(ownerUserId, parsedCount, importedCount, skippedCount, isCompleted: false, isFailed: false, message: null, cancellationToken);
                    }
                }
            }

            if (batch.Count > 0)
            {
                await PersistBatchAsync(batch, ownerUserId, now, cancellationToken);
                batch.Clear();
            }

            await transaction.CommitAsync(cancellationToken);
            await PublishProgressAsync(ownerUserId, parsedCount, importedCount, skippedCount, isCompleted: false, isFailed: false, message: "Finalizing system database...", cancellationToken);
            return new DraftImportResult(parsedCount, importedCount, skippedCount);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            await PublishProgressAsync(ownerUserId, parsedCount, importedCount, skippedCount, isCompleted: true, isFailed: true, message: "Import failed.", cancellationToken);
            throw;
        }
    }

    private async Task PublishProgressAsync(
        string ownerUserId,
        int parsedCount,
        int importedCount,
        int skippedCount,
        bool isCompleted,
        bool isFailed,
        string? message,
        CancellationToken cancellationToken)
    {
        if (progressPublisher is null)
        {
            return;
        }

        var update = new DraftImportProgressUpdate(
            parsedCount,
            importedCount,
            skippedCount,
            isCompleted,
            isFailed,
            message);

        await progressPublisher.PublishAsync(ownerUserId, update, cancellationToken);
    }

    private async Task PersistBatchAsync(
        IReadOnlyCollection<ParsedGame> parsedGames,
        string ownerUserId,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        await positionImportCoordinator.PopulateAsync(parsedGames, cancellationToken);

        var stagingGames = parsedGames.Select(parsed => MapToStagingGame(parsed, ownerUserId, createdAtUtc)).ToArray();

        await draftImportRepository.AddStagingGamesAsync(stagingGames, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        unitOfWork.ClearTracker();
    }

    internal static StagingGame MapToStagingGame(ParsedGame parsedGame, string ownerUserId, DateTime createdAtUtc)
    {
        var game = ParsedGameFinalizer.Finalize(parsedGame);

        var stagingGame = new StagingGame
        {
            Id = game.Id,
            OwnerUserId = ownerUserId,
            CreatedAtUtc = createdAtUtc,
            Date = game.Date,
            Year = game.Year,
            Round = game.Round,
            WhiteTitle = game.WhiteTitle,
            BlackTitle = game.BlackTitle,
            WhiteElo = game.WhiteElo,
            BlackElo = game.BlackElo,
            Event = game.Event,
            Site = game.Site,
            TimeControl = game.TimeControl,
            ECO = game.ECO,
            Opening = game.Opening,
            White = game.White,
            Black = game.Black,
            WhiteNormalizedFullName = game.WhiteNormalizedFullName,
            WhiteNormalizedFirstName = game.WhiteNormalizedFirstName,
            WhiteNormalizedLastName = game.WhiteNormalizedLastName,
            BlackNormalizedFullName = game.BlackNormalizedFullName,
            BlackNormalizedFirstName = game.BlackNormalizedFirstName,
            BlackNormalizedLastName = game.BlackNormalizedLastName,
            Result = game.Result,
            Pgn = game.Pgn,
            MoveCount = game.MoveCount,
            GameHash = game.GameHash
        };

        stagingGame.Positions = game.Positions
            .Select(p => new StagingPosition
            {
                StagingGameId = stagingGame.Id,
                PlyCount = p.PlyCount,
                PosKey = p.PosKey,
                NextMove = p.NextMove,
                Result = p.Result
            })
            .ToArray();

        return stagingGame;
    }
}
