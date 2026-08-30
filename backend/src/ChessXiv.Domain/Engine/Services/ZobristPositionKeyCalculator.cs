using System.Buffers.Binary;
using System.Numerics;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Hashing;
using ChessXiv.Domain.Engine.Models;
using ChessXiv.Domain.Engine.Types;

namespace ChessXiv.Domain.Engine.Services;

/// <summary>
/// Builds both halves of the position key in a single pass over the piece bitboards, so
/// identifying a position costs no more than the old single-hash path and, unlike the
/// previous approach, never has to build a FEN string.
/// </summary>
public sealed class ZobristPositionKeyCalculator : IPositionKeyCalculator
{
    public const int KeyLength = 16;

    public byte[] Compute(in BoardState state)
    {
        var low = 0UL;
        var high = 0UL;

        for (var pieceIndex = 0; pieceIndex < BoardState.PieceBitboardCount; pieceIndex++)
        {
            var bb = state.PieceBitboards[pieceIndex].Value;
            while (bb != 0UL)
            {
                var square = BitOperations.TrailingZeroCount(bb);
                bb &= bb - 1;
                low ^= ZobristTables.PieceSquare[pieceIndex, square];
                high ^= ZobristTables.PieceSquareHigh[pieceIndex, square];
            }
        }

        if (state.SideToMove == Color.Black)
        {
            low ^= ZobristTables.SideToMove;
            high ^= ZobristTables.SideToMoveHigh;
        }

        var castling = state.CastlingRights.Value & 0x0F;
        low ^= ZobristTables.CastlingRights[castling];
        high ^= ZobristTables.CastlingRightsHigh[castling];

        if (state.EnPassantSquare.HasValue)
        {
            var file = state.EnPassantSquare.Value.File;
            low ^= ZobristTables.EnPassantFile[file];
            high ^= ZobristTables.EnPassantFileHigh[file];
        }

        var key = new byte[KeyLength];
        BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(0, 8), low);
        BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(8, 8), high);
        return key;
    }
}
