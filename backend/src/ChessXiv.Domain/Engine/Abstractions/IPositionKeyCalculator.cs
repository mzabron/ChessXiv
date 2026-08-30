using ChessXiv.Domain.Engine.Models;

namespace ChessXiv.Domain.Engine.Abstractions;

public interface IPositionKeyCalculator
{
    /// <summary>
    /// The 16-byte identity of a position: pieces, side to move, castling rights and the
    /// en-passant file. Deliberately excludes the halfmove clock and move number, so a
    /// position reached by a different move order matches - which is what a chess database
    /// user means by "the same position".
    /// </summary>
    byte[] Compute(in BoardState state);
}
