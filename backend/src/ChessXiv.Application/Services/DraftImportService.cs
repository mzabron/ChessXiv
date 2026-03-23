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
    IUnitOfWork unitOfWork) : IDraftImportService
{
    private static readonly TimeSpan DefaultDraftTtl = TimeSpan.FromDays(7);

    public async Task<DraftImportResult> ImportAsync(
        TextReader reader,
        string ownerUserId,
        Guid? importSessionId = null,
        int batchSize = 500,
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
        var session = await GetOrCreateSessionAsync(ownerUserId, importSessionId, now, cancellationToken);
        var maxDraftImportGames = await quotaService.GetMaxDraftImportGamesAsync(ownerUserId, cancellationToken);
        var existingCount = await draftImportRepository.CountStagingGamesAsync(session.Id, ownerUserId, cancellationToken);

        if (existingCount >= maxDraftImportGames)
        {
            return new DraftImportResult(session.Id, 0, 0, 0, session.ExpiresAtUtc);
        }

        var parsedCount = 0;
        var importedCount = 0;
        var skippedCount = 0;
        var batch = new List<StagingGame>(batchSize);
        var remainingCapacity = maxDraftImportGames - existingCount;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
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

                var stagingGame = MapToStagingGame(parsedGame, session.Id, ownerUserId);
                batch.Add(stagingGame);
                importedCount++;

                if (batch.Count >= batchSize)
                {
                    await PersistBatchAsync(batch, cancellationToken);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await PersistBatchAsync(batch, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new DraftImportResult(session.Id, parsedCount, importedCount, skippedCount, session.ExpiresAtUtc);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<StagingImportSession> GetOrCreateSessionAsync(
        string ownerUserId,
        Guid? importSessionId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (importSessionId.HasValue)
        {
            var existingSession = await draftImportRepository
                .GetImportSessionAsync(importSessionId.Value, ownerUserId, cancellationToken);

            if (existingSession is null)
            {
                throw new InvalidOperationException("Draft import session was not found for this user.");
            }

            if (existingSession.PromotedAtUtc.HasValue)
            {
                throw new InvalidOperationException("Draft import session has already been promoted.");
            }

            if (existingSession.ExpiresAtUtc <= now)
            {
                throw new InvalidOperationException("Draft import session has expired.");
            }

            return existingSession;
        }

        var session = new StagingImportSession
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(DefaultDraftTtl)
        };

        await draftImportRepository.DeleteUnpromotedSessionsByOwnerAsync(ownerUserId, cancellationToken);
        await draftImportRepository.AddImportSessionAsync(session, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return session;
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

    internal static StagingGame MapToStagingGame(Game game, Guid importSessionId, string ownerUserId)
    {
        if (game.Date.HasValue)
        {
            game.Year = game.Date.Value.Year;
        }

        game.MoveCount = game.Moves.Count;

        return new StagingGame
        {
            Id = game.Id,
            ImportSessionId = importSessionId,
            OwnerUserId = ownerUserId,
            WhitePlayerId = game.WhitePlayerId,
            BlackPlayerId = game.BlackPlayerId,
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
