using ChessXiv.Domain.Engine.Models;
using ChessXiv.Domain.Engine.Types;

namespace ChessXiv.Domain.Engine.Rules;

/// <summary>
/// Decides whether a position actually has an en-passant target.
/// </summary>
/// <remarks>
/// The bare FEN grammar allows recording a target square after every double pawn push, but
/// doing so is wrong for a chess database. The en-passant square is part of a position's
/// identity, so a square recorded when no capture is available splits one position into two
/// different keys depending on the move order that reached it - precisely the transpositions
/// the opening tree exists to merge.
///
/// For example 1.e4 e5 2.Nf3 Nc6 and 1.Nf3 Nc6 2.e4 e5 reach the same placement; only the
/// halfmove clock differs (2 vs 0), and that is not part of the position. Both must have "-"
/// in the en-passant field and therefore the same key.
///
/// The test is "an enemy pawn stands beside the pushed pawn". A pin could still make the
/// capture illegal, but a pin is equally a property of the position, so keys stay
/// path-independent either way.
/// </remarks>
public static class EnPassantRules
{
    private const ulong FileA = 0x0101010101010101UL;

    /// <summary>
    /// The en-passant square to record after a pawn double-pushed to <paramref name="to"/>,
    /// or null when no enemy pawn can take it.
    /// </summary>
    public static Square? ResolveAfterDoublePush(BoardState state, bool movedWhite, Square to)
    {
        if (!HasAdjacentEnemyPawn(state, movedWhite, to))
        {
            return null;
        }

        var skippedRank = movedWhite ? to.Rank - 1 : to.Rank + 1;
        return Square.From(to.File, skippedRank);
    }

    /// <summary>
    /// Drops an en-passant square that no pawn can actually capture on. Applied when reading
    /// a FEN so that a hand-written or third-party FEN yields the same key as the same
    /// position produced by replaying a game.
    /// </summary>
    public static Square? Normalize(BoardState state, Square? enPassantSquare)
    {
        if (!enPassantSquare.HasValue)
        {
            return null;
        }

        var square = enPassantSquare.Value;

        // The target square sits behind the pawn that pushed: rank index 2 (rank 3) means
        // White pushed, rank index 5 (rank 6) means Black did.
        var movedWhite = square.Rank == 2;
        if (!movedWhite && square.Rank != 5)
        {
            return null;
        }

        var pawnRank = movedWhite ? 3 : 4;
        return HasAdjacentEnemyPawn(state, movedWhite, Square.From(square.File, pawnRank))
            ? square
            : null;
    }

    private static bool HasAdjacentEnemyPawn(BoardState state, bool movedWhite, Square pawnSquare)
    {
        var enemyPawn = movedWhite ? Piece.BlackPawn : Piece.WhitePawn;
        var enemyPawns = state.PieceBitboards[(int)enemyPawn - 1].Value;

        var rankMask = 0xFFUL << (pawnSquare.Rank * 8);

        var adjacentFiles = 0UL;
        if (pawnSquare.File > 0)
        {
            adjacentFiles |= FileA << (pawnSquare.File - 1);
        }

        if (pawnSquare.File < 7)
        {
            adjacentFiles |= FileA << (pawnSquare.File + 1);
        }

        return (enemyPawns & rankMask & adjacentFiles) != 0UL;
    }
}
