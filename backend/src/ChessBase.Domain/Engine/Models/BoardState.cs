using ChessBase.Domain.Engine.Types;

namespace ChessBase.Domain.Engine.Models;

public sealed class BoardState
{
    public const int PieceBitboardCount = 12;

    public BoardState()
    {
        PieceBitboards = new Bitboard[PieceBitboardCount];
    }

    public Bitboard[] PieceBitboards { get; }

    public Bitboard WhiteOccupancy { get; set; }

    public Bitboard BlackOccupancy { get; set; }

    public Bitboard AllOccupancy => WhiteOccupancy | BlackOccupancy;

    public Color SideToMove { get; set; } = Color.White;

    public CastlingRights CastlingRights { get; set; } = CastlingRights.All;

    public Square? EnPassantSquare { get; set; }

    public int HalfMoveClock { get; set; }

    public int FullMoveNumber { get; set; } = 1;

    public ulong ZobristKey { get; set; }
}
