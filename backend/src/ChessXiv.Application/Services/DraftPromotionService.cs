using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Services;

public sealed class DraftPromotionService(
    IDraftPromotionRepository draftPromotionRepository,
    IUnitOfWork unitOfWork) : IDraftPromotionService
{
    private const int PromotionBatchSize = 500;

    public async Task<DraftPromotionResult> PromoteAsync(
        string ownerUserId,
        Guid userDatabaseId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
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

        var now = DateTime.UtcNow;
        var promotedCount = 0;
        var skippedCount = 0;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            while (true)
            {
                var stagingPage = await draftPromotionRepository
                    .GetStagingGamesPageAsync(ownerUserId, PromotionBatchSize, cancellationToken);

                if (stagingPage.Count == 0)
                {
                    break;
                }

                var gamesToPromote = new List<Game>(stagingPage.Count);
                var stagingIdsToDelete = new List<Guid>(stagingPage.Count);

                foreach (var stagingGame in stagingPage)
                {
                    var promotedGame = MapToMainGame(stagingGame);
                    gamesToPromote.Add(promotedGame);
                    stagingIdsToDelete.Add(stagingGame.Id);
                }

                if (gamesToPromote.Count > 0)
                {
                    foreach (var promotedGame in gamesToPromote)
                    {
                        await draftPromotionRepository.AddGameAsync(promotedGame, cancellationToken);
                        await draftPromotionRepository.AddUserDatabaseGameAsync(new UserDatabaseGame
                        {
                            UserDatabaseId = userDatabaseId,
                            GameId = promotedGame.Id,
                            AddedAtUtc = now,
                            Date = promotedGame.Date,
                            Year = promotedGame.Year,
                            Event = promotedGame.Event,
                            Round = promotedGame.Round,
                            Site = promotedGame.Site
                        }, cancellationToken);

                        promotedCount++;
                    }
                }

                await draftPromotionRepository.RemoveStagingGamesAsync(stagingIdsToDelete, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                unitOfWork.ClearTracker();
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new DraftPromotionResult(promotedCount, skippedCount);
    }

    private static Game MapToMainGame(StagingGame stagingGame)
    {
        return new Game
        {
            Id = Guid.NewGuid(),
            Date = stagingGame.Date,
            Year = stagingGame.Year,
            Round = stagingGame.Round,
            WhiteTitle = stagingGame.WhiteTitle,
            BlackTitle = stagingGame.BlackTitle,
            WhiteElo = stagingGame.WhiteElo,
            BlackElo = stagingGame.BlackElo,
            Event = stagingGame.Event,
            Site = stagingGame.Site,
            TimeControl = stagingGame.TimeControl,
            ECO = stagingGame.ECO,
            Opening = stagingGame.Opening,
            White = stagingGame.White,
            Black = stagingGame.Black,
            WhiteNormalizedFullName = stagingGame.WhiteNormalizedFullName,
            WhiteNormalizedFirstName = stagingGame.WhiteNormalizedFirstName,
            WhiteNormalizedLastName = stagingGame.WhiteNormalizedLastName,
            BlackNormalizedFullName = stagingGame.BlackNormalizedFullName,
            BlackNormalizedFirstName = stagingGame.BlackNormalizedFirstName,
            BlackNormalizedLastName = stagingGame.BlackNormalizedLastName,
            Result = stagingGame.Result,
            Pgn = stagingGame.Pgn,
            MoveCount = stagingGame.MoveCount,
            GameHash = stagingGame.GameHash,
            Moves = stagingGame.Moves.Select(m => new Move
            {
                Id = Guid.NewGuid(),
                MoveNumber = m.MoveNumber,
                WhiteMove = m.WhiteMove,
                BlackMove = m.BlackMove,
                WhiteClk = m.WhiteClk,
                BlackClk = m.BlackClk,
                WhiteEval = m.WhiteEval,
                BlackEval = m.BlackEval
            }).ToArray(),
            Positions = stagingGame.Positions.Select(p => new Position
            {
                Id = Guid.NewGuid(),
                Fen = p.Fen,
                FenHash = p.FenHash,
                PlyCount = p.PlyCount,
                LastMove = p.LastMove,
                SideToMove = p.SideToMove
            }).ToArray()
        };
    }
}
