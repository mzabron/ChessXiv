using ChessBase.Application.Abstractions;
using ChessBase.Domain.Engine.Abstractions;
using ChessBase.Domain.Engine.Models;
using ChessBase.Domain.Engine.Types;
using ChessBase.Domain.Entities;

namespace ChessBase.Application.Services;

public class PositionImportCoordinator(
    IBoardStateFactory boardStateFactory,
    IBoardStateSerializer boardStateSerializer,
    IBoardStateTransition boardStateTransition,
    IPositionHasher positionHasher) : IPositionImportCoordinator
{
    public Task PopulateAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default)
    {
        if (games.Count == 0)
        {
            return Task.CompletedTask;
        }

        var initialStateTemplate = boardStateFactory.CreateInitial();
        initialStateTemplate.ZobristKey = positionHasher.Compute(initialStateTemplate);

        foreach (var game in games)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PopulateSingleGame(game, initialStateTemplate);
        }

        return Task.CompletedTask;
    }

    private void PopulateSingleGame(Game game, BoardState initialStateTemplate)
    {
        game.Positions.Clear();

        var state = CloneState(initialStateTemplate);
        AddPosition(game, state, plyCount: 0, lastMove: null);

        var plyCount = 0;
        foreach (var move in game.Moves.OrderBy(m => m.MoveNumber))
        {
            if (!string.IsNullOrWhiteSpace(move.WhiteMove))
            {
                if (!boardStateTransition.TryApplySan(state, move.WhiteMove))
                {
                    break;
                }

                plyCount++;
                AddPosition(game, state, plyCount, move.WhiteMove);
            }

            if (!string.IsNullOrWhiteSpace(move.BlackMove))
            {
                if (!boardStateTransition.TryApplySan(state, move.BlackMove))
                {
                    break;
                }

                plyCount++;
                AddPosition(game, state, plyCount, move.BlackMove);
            }
        }
    }

    private void AddPosition(Game game, BoardState state, int plyCount, string? lastMove)
    {
        var fen = boardStateSerializer.ToFen(state);

        game.Positions.Add(new Position
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Fen = fen,
            FenHash = unchecked((long)state.ZobristKey),
            PlyCount = plyCount,
            LastMove = lastMove,
            SideToMove = state.SideToMove == Color.White ? 'w' : 'b'
        });
    }

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
