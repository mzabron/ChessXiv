using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Models;
using ChessXiv.Domain.Engine.Types;
using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Services;

public class PositionImportCoordinator(
    IBoardStateFactory boardStateFactory,
    IBoardStateTransition boardStateTransition,
    IPositionKeyCalculator positionKeyCalculator) : IPositionImportCoordinator
{
    /// <summary>
    /// Below this many games per batch the coordination overhead outweighs the gain, so the
    /// work stays on the calling thread.
    /// </summary>
    private const int ParallelThreshold = 32;

    public Task PopulateAsync(IReadOnlyCollection<ParsedGame> games, CancellationToken cancellationToken = default)
    {
        if (games.Count == 0)
        {
            return Task.CompletedTask;
        }

        var initialStateTemplate = boardStateFactory.CreateInitial();

        // Replaying a game is CPU-bound and each game is completely independent of the
        // others, so a batch fans out across cores. This is the dominant cost of an import.
        if (games.Count >= ParallelThreshold)
        {
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            Parallel.ForEach(games, parallelOptions, game => PopulateSingleGame(game, initialStateTemplate));
            return Task.CompletedTask;
        }

        foreach (var game in games)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PopulateSingleGame(game, initialStateTemplate);
        }

        return Task.CompletedTask;
    }

    private void PopulateSingleGame(ParsedGame parsedGame, BoardState initialStateTemplate)
    {
        var game = parsedGame.Game;
        var result = ToGameResult(game.Result);
        var positions = new List<Position>(parsedGame.Moves.Count * 2 + 1);

        var state = CloneState(initialStateTemplate);
        positions.Add(CreatePosition(game.Id, state, plyCount: 0, result));

        var plyCount = 0;
        foreach (var move in parsedGame.Moves.OrderBy(m => m.MoveNumber))
        {
            if (!string.IsNullOrWhiteSpace(move.WhiteMove))
            {
                // The move is recorded on the position it was played *from*, which is the
                // previous row; that is what makes the opening tree a single index scan.
                positions[^1].NextMove = move.WhiteMove;

                if (!boardStateTransition.TryApplySan(state, move.WhiteMove))
                {
                    positions[^1].NextMove = null;
                    break;
                }

                plyCount++;
                positions.Add(CreatePosition(game.Id, state, plyCount, result));
            }

            if (!string.IsNullOrWhiteSpace(move.BlackMove))
            {
                positions[^1].NextMove = move.BlackMove;

                if (!boardStateTransition.TryApplySan(state, move.BlackMove))
                {
                    positions[^1].NextMove = null;
                    break;
                }

                plyCount++;
                positions.Add(CreatePosition(game.Id, state, plyCount, result));
            }
        }

        game.Positions = positions;
    }

    private Position CreatePosition(Guid gameId, BoardState state, int plyCount, GameResult result)
    {
        return new Position
        {
            GameId = gameId,
            PlyCount = (short)plyCount,
            PosKey = positionKeyCalculator.Compute(state),
            NextMove = null,
            Result = result
        };
    }

    internal static GameResult ToGameResult(string? result) => result switch
    {
        "1-0" => GameResult.WhiteWin,
        "0-1" => GameResult.BlackWin,
        "1/2-1/2" => GameResult.Draw,
        _ => GameResult.Unknown
    };

    private static BoardState CloneState(BoardState source)
    {
        var clone = new BoardState
        {
            WhiteOccupancy = source.WhiteOccupancy,
            BlackOccupancy = source.BlackOccupancy,
            SideToMove = source.SideToMove,
            CastlingRights = source.CastlingRights,
            EnPassantSquare = source.EnPassantSquare,
            HalfMoveClock = source.HalfMoveClock,
            FullMoveNumber = source.FullMoveNumber,
            ZobristKey = source.ZobristKey
        };

        for (var i = 0; i < BoardState.PieceBitboardCount; i++)
        {
            clone.PieceBitboards[i] = source.PieceBitboards[i];
        }

        return clone;
    }
}
