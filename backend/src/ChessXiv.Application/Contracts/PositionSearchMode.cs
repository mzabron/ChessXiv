namespace ChessXiv.Application.Contracts;

/// <summary>
/// How a position filter matches stored positions.
/// </summary>
public enum PositionSearchMode
{
    /// <summary>
    /// Any game that ever reached this position, whatever the move order. Matches on the
    /// position key alone, so transpositions are found. This is the default.
    /// </summary>
    SamePosition = 0,

    /// <summary>
    /// The same position reached at the same point in the game. Matches on the position key
    /// plus the ply, which is stored on every position row anyway, so this costs nothing
    /// extra. Note this is a ply filter, not a full-FEN comparison: the halfmove clock is
    /// not part of a position and is deliberately ignored.
    /// </summary>
    ExactPly = 1
}
