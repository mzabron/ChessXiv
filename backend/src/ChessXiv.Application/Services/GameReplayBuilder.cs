using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Models;

namespace ChessXiv.Application.Services;

public sealed class GameReplayBuilder(
    IPgnParser pgnParser,
    IBoardStateFactory boardStateFactory,
    IBoardStateSerializer boardStateSerializer,
    IBoardStateTransition boardStateTransition) : IGameReplayBuilder
{
    public GameReplay Build(string? pgn)
    {
        var state = boardStateFactory.CreateInitial();
        var startFen = boardStateSerializer.ToFen(state);

        if (string.IsNullOrWhiteSpace(pgn))
        {
            return new GameReplay([startFen], []);
        }

        var parsedGame = pgnParser.ParsePgn(pgn).FirstOrDefault();
        if (parsedGame is null)
        {
            return new GameReplay([startFen], []);
        }

        var moves = parsedGame.Moves
            .OrderBy(m => m.MoveNumber)
            .ToArray();

        var fenHistory = new List<string>(moves.Length * 2 + 1) { startFen };

        foreach (var move in moves)
        {
            if (!TryAdvance(state, move.WhiteMove, fenHistory))
            {
                break;
            }

            if (!TryAdvance(state, move.BlackMove, fenHistory))
            {
                break;
            }
        }

        var moveDtos = moves
            .Select(m => new GameReplayMoveDto(
                m.MoveNumber,
                m.WhiteMove,
                m.BlackMove,
                string.IsNullOrWhiteSpace(m.WhiteClk) ? null : m.WhiteClk,
                string.IsNullOrWhiteSpace(m.BlackClk) ? null : m.BlackClk))
            .ToArray();

        return new GameReplay(fenHistory, moveDtos);
    }

    private bool TryAdvance(BoardState state, string? san, List<string> fenHistory)
    {
        if (string.IsNullOrWhiteSpace(san))
        {
            return true;
        }

        if (!boardStateTransition.TryApplySan(state, san))
        {
            return false;
        }

        fenHistory.Add(boardStateSerializer.ToFen(state));
        return true;
    }
}
