using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions;

public interface IGameReplayBuilder
{
    /// <summary>
    /// Rebuilds a game's move list and FEN-per-ply history from its PGN.
    /// </summary>
    /// <remarks>
    /// Both used to be stored: the moves in their own table and a FEN string on every
    /// position row. Together they dominated the database size while being fully derivable
    /// from the PGN, which is stored anyway. Replaying one game costs well under a
    /// millisecond and only happens when a user opens that single game.
    /// </remarks>
    GameReplay Build(string? pgn);
}

public sealed record GameReplay(
    IReadOnlyList<string> FenHistory,
    IReadOnlyList<GameReplayMoveDto> Moves);
