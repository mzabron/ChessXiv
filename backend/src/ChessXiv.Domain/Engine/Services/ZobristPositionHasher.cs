using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Hashing;
using ChessXiv.Domain.Engine.Models;
using System.Numerics;

namespace ChessXiv.Domain.Engine.Services;

public sealed class ZobristPositionHasher : IPositionHasher
{
    public ulong Compute(in BoardState state)
    {
        ulong hash = 0UL;

        for (var pieceIndex = 0; pieceIndex < BoardState.PieceBitboardCount; pieceIndex++)
        {
            var bb = state.PieceBitboards[pieceIndex].Value;
            while (bb != 0UL)
            {
                var square = BitOperations.TrailingZeroCount(bb);
                bb &= bb - 1;
                hash ^= ZobristTables.PieceSquare[pieceIndex, square];
            }
        }

        if (state.SideToMove == Types.Color.Black)
        {
            hash ^= ZobristTables.SideToMove;
        }

        hash ^= ZobristTables.CastlingRights[state.CastlingRights.Value & 0x0F];

        if (state.EnPassantSquare.HasValue)
        {
            hash ^= ZobristTables.EnPassantFile[state.EnPassantSquare.Value.File];
        }

        return hash;
    }
}
