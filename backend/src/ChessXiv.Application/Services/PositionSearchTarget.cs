using ChessXiv.Domain.Engine.Abstractions;

namespace ChessXiv.Application.Services;

/// <summary>
/// A FEN resolved into what the database actually stores: the position key, plus the ply it
/// represents for callers filtering on a specific point in the game.
/// </summary>
public sealed record PositionSearchTarget(byte[] PosKey, int PlyCount)
{
    /// <summary>
    /// Returns null when position search is off or the FEN cannot be parsed, which leaves the
    /// position filter inactive rather than failing the whole request.
    /// </summary>
    public static PositionSearchTarget? Resolve(
        bool searchByPosition,
        string? fen,
        IBoardStateSerializer boardStateSerializer,
        IPositionKeyCalculator positionKeyCalculator)
    {
        if (!searchByPosition || string.IsNullOrWhiteSpace(fen))
        {
            return null;
        }

        try
        {
            var state = boardStateSerializer.FromFen(fen.Trim());

            // Ply 0 is the initial position. A FEN's fullmove number counts from 1 and only
            // advances after Black moves, so the ply is derived from it and the side to move.
            var plyCount = ((state.FullMoveNumber - 1) * 2)
                + (state.SideToMove == Domain.Engine.Types.Color.Black ? 1 : 0);

            return new PositionSearchTarget(positionKeyCalculator.Compute(state), plyCount);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
