using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Services;

public sealed class DraftImportService(
    IPgnParser pgnParser,
    IPositionImportCoordinator positionImportCoordinator,
    IDraftImportRepository draftImportRepository,
    IQuotaService quotaService,
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
        var maxDraftImportGames = await quotaService.GetMaxDraftImportGamesAsync(ownerUserId, cancellationToken);

        if (maxDraftImportGames <= 0)
        {
            return new DraftImportResult(0, 0, 0);
        }

        var parsedCount = 0;
        var importedCount = 0;
        var skippedCount = 0;
        var batch = new List<StagingGame>(batchSize);
        var remainingCapacity = maxDraftImportGames;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await draftImportRepository.ClearStagingGamesAsync(ownerUserId, cancellationToken);
            await PublishProgressAsync(ownerUserId, parsedCount, importedCount, skippedCount, isCompleted: false, isFailed: false, message: "Import started.", cancellationToken);

            await foreach (var parsedGame in pgnParser.ParsePgnAsync(reader, cancellationToken))
            {
                parsedCount++;
                if (string.IsNullOrWhiteSpace(parsedGame.White) || string.IsNullOrWhiteSpace(parsedGame.Black))
                {
                    skippedCount++;
                    continue;
                }

                if (importedCount >= remainingCapacity)
                {
                    throw new InvalidOperationException($"Import exceeds allowed draft quota ({maxDraftImportGames} games). No games were imported.");
                }

                var stagingGame = MapToStagingGame(parsedGame, ownerUserId, now);
                batch.Add(stagingGame);
                importedCount++;

                if (batch.Count >= batchSize)
                {
                    await PersistBatchAsync(batch, cancellationToken);
                    batch.Clear();
                    await PublishProgressAsync(ownerUserId, parsedCount, importedCount, skippedCount, isCompleted: false, isFailed: false, message: null, cancellationToken);
                }
                else if (parsedCount % 10 == 0)
                {
                    await PublishProgressAsync(ownerUserId, parsedCount, importedCount, skippedCount, isCompleted: false, isFailed: false, message: null, cancellationToken);
                }
            }

            if (batch.Count > 0)
            {
                await PersistBatchAsync(batch, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            await PublishProgressAsync(ownerUserId, parsedCount, importedCount, skippedCount, isCompleted: true, isFailed: false, message: "Import completed.", cancellationToken);
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

    private async Task PersistBatchAsync(IReadOnlyCollection<StagingGame> stagingGames, CancellationToken cancellationToken)
    {
        var stagingArray = stagingGames as StagingGame[] ?? stagingGames.ToArray();
        var transientGames = stagingArray.Select(MapToTransientGame).ToArray();
        await positionImportCoordinator.PopulateAsync(transientGames, cancellationToken);

        for (var i = 0; i < stagingArray.Length; i++)
        {
            var stagingGame = stagingArray[i];
            var transientGame = transientGames[i];

            stagingGame.Positions = transientGame.Positions.Select(p => new StagingPosition
            {
                Id = Guid.NewGuid(),
                StagingGameId = stagingGame.Id,
                Fen = p.Fen,
                FenHash = p.FenHash,
                PlyCount = p.PlyCount,
                LastMove = p.LastMove,
                SideToMove = p.SideToMove
            }).ToArray();
        }

        await draftImportRepository.AddStagingGamesAsync(stagingArray, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        unitOfWork.ClearTracker();
    }

    internal static StagingGame MapToStagingGame(Game game, string ownerUserId, DateTime createdAtUtc)
    {
        if (game.Date.HasValue)
        {
            game.Year = game.Date.Value.Year;
        }

        game.MoveCount = game.Moves.Count;
        ApplyNormalizedNames(game);

        return new StagingGame
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
            GameHash = GameHashCalculator.Compute(game),
            Moves = game.Moves.Select(m => new StagingMove
            {
                Id = m.Id,
                StagingGameId = game.Id,
                MoveNumber = m.MoveNumber,
                WhiteMove = m.WhiteMove,
                BlackMove = m.BlackMove,
                WhiteClk = m.WhiteClk,
                BlackClk = m.BlackClk,
                WhiteEval = m.WhiteEval,
                BlackEval = m.BlackEval
            }).ToArray()
        };
    }

    private static void ApplyNormalizedNames(Game game)
    {
        ApplyNormalizedName(game.White, out var whiteFull, out var whiteFirst, out var whiteLast);
        ApplyNormalizedName(game.Black, out var blackFull, out var blackFirst, out var blackLast);

        game.WhiteNormalizedFullName = whiteFull;
        game.WhiteNormalizedFirstName = whiteFirst;
        game.WhiteNormalizedLastName = whiteLast;
        game.BlackNormalizedFullName = blackFull;
        game.BlackNormalizedFirstName = blackFirst;
        game.BlackNormalizedLastName = blackLast;
    }

    private static void ApplyNormalizedName(string rawName, out string full, out string? first, out string? last)
    {
        var (parsedFirst, parsedLast) = PlayerNameNormalizer.ParseNameParts(rawName);
        first = parsedFirst is null ? null : PlayerNameNormalizer.Normalize(parsedFirst);
        last = parsedLast is null ? null : PlayerNameNormalizer.Normalize(parsedLast);

        if (first is not null && last is not null)
        {
            full = PlayerNameNormalizer.Normalize($"{parsedFirst} {parsedLast}");
            return;
        }

        full = PlayerNameNormalizer.Normalize(rawName);
    }

    internal static Game MapToTransientGame(StagingGame stagingGame)
    {
        return new Game
        {
            Id = stagingGame.Id,
            White = stagingGame.White,
            Black = stagingGame.Black,
            Result = stagingGame.Result,
            Pgn = stagingGame.Pgn,
            Moves = stagingGame.Moves.Select(m => new Move
            {
                Id = m.Id,
                GameId = stagingGame.Id,
                MoveNumber = m.MoveNumber,
                WhiteMove = m.WhiteMove,
                BlackMove = m.BlackMove,
                WhiteClk = m.WhiteClk,
                BlackClk = m.BlackClk,
                WhiteEval = m.WhiteEval,
                BlackEval = m.BlackEval
            }).ToArray()
        };
    }
}
