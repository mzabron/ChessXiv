using ChessBase.Domain.Engine.Abstractions;
using ChessBase.Domain.Engine.Hashing;
using ChessBase.Domain.Engine.Models;
using ChessBase.Domain.Engine.Types;
using System.Numerics;
using System.Text.RegularExpressions;

namespace ChessBase.Domain.Engine.Services;

public sealed class BitboardBoardStateTransition : IBoardStateTransition
{
    private static readonly Regex PromotionRegex = new("=([NBRQ])", RegexOptions.Compiled);

    public bool TryApplySan(BoardState state, string san)
    {
        if (string.IsNullOrWhiteSpace(san))
        {
            return false;
        }

        var normalized = NormalizeSan(san);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized is "O-O" or "0-0")
        {
            return ApplyCastle(state, kingSide: true);
        }

        if (normalized is "O-O-O" or "0-0-0")
        {
            return ApplyCastle(state, kingSide: false);
        }

        return ApplyStandardMove(state, normalized);
    }

    private static bool ApplyStandardMove(BoardState state, string san)
    {
        var promotionMatch = PromotionRegex.Match(san);
        PieceType promotion = PieceType.None;
        if (promotionMatch.Success)
        {
            promotion = promotionMatch.Groups[1].Value[0] switch
            {
                'N' => PieceType.Knight,
                'B' => PieceType.Bishop,
                'R' => PieceType.Rook,
                _ => PieceType.Queen //underscore represents default value in switch expression
            };

            san = san.Remove(promotionMatch.Index, promotionMatch.Length);
        }

        if (san.Length < 2)
        {
            return false;
        }

        var destFileChar = san[^2];
        var destRankChar = san[^1];
        if (destFileChar is < 'a' or > 'h' || destRankChar is < '1' or > '8')
        {
            return false;
        }

        var to = Square.From(destFileChar - 'a', destRankChar - '1');
        var prefix = san[..^2];
        var isCapture = prefix.Contains('x', StringComparison.Ordinal);

        var pieceLetter = 'P';
        var prefixStart = 0;
        if (prefix.Length > 0 && "KQRBN".Contains(prefix[0], StringComparison.Ordinal))
        {
            pieceLetter = prefix[0];
            prefixStart = 1;
        }

        var descriptor = prefix[prefixStart..].Replace("x", string.Empty, StringComparison.Ordinal);
        int? fromFileConstraint = null;
        int? fromRankConstraint = null;

        foreach (var ch in descriptor)
        {
            if (ch is >= 'a' and <= 'h')
            {
                fromFileConstraint = ch - 'a';
            }
            else if (ch is >= '1' and <= '8')
            {
                fromRankConstraint = ch - '1';
            }
            else
            {
                return false;
            }
        }

        var mover = PieceFor(state.SideToMove, pieceLetter);
        var candidates = FindCandidates(state, mover, to, isCapture, fromFileConstraint, fromRankConstraint, promotion != PieceType.None);
        var legalCandidates = candidates
            .Where(from => IsLegalAfterMove(state, from, to, mover, isCapture, promotion))
            .ToList();

        if (legalCandidates.Count != 1)
        {
            return false;
        }

        ApplyMove(state, legalCandidates[0], to, mover, isCapture, promotion);
        return true;
    }

    private static List<Square> FindCandidates(
        BoardState state,
        Piece mover,
        Square to,
        bool isCapture,
        int? fromFileConstraint,
        int? fromRankConstraint,
        bool isPromotion)
    {
        var candidates = new List<Square>(8);
        var bb = state.PieceBitboards[PieceToIndex(mover)].Value;

        while (bb != 0UL)
        {
            var fromIndex = BitOperations.TrailingZeroCount(bb);
            bb &= bb - 1;

            var from = new Square((byte)fromIndex);
            if (fromFileConstraint.HasValue && from.File != fromFileConstraint.Value)
            {
                continue;
            }

            if (fromRankConstraint.HasValue && from.Rank != fromRankConstraint.Value)
            {
                continue;
            }

            if (CanPieceReach(state, from, to, mover, isCapture, isPromotion))
            {
                candidates.Add(from);
            }
        }

        return candidates;
    }

    private static bool CanPieceReach(BoardState state, Square from, Square to, Piece mover, bool isCapture, bool isPromotion)
    {
        var target = GetPieceAt(state, to);
        if (target != Piece.None && IsWhite(target) == IsWhite(mover))
        {
            return false;
        }

        var fileDelta = to.File - from.File;
        var rankDelta = to.Rank - from.Rank;

        switch (ToPieceType(mover))
        {
            case PieceType.Pawn:
            {
                var white = IsWhite(mover);
                var step = white ? 1 : -1;
                var startRank = white ? 1 : 6;
                var promotionRank = white ? 7 : 0;

                if (isCapture)
                {
                    if (Math.Abs(fileDelta) != 1 || rankDelta != step)
                    {
                        return false;
                    }

                    if (target != Piece.None)
                    {
                        return true;
                    }

                    if (!state.EnPassantSquare.HasValue)
                    {
                        return false;
                    }

                    return state.EnPassantSquare.Value.Index == to.Index;
                }

                if (fileDelta != 0)
                {
                    return false;
                }

                if (rankDelta == step && target == Piece.None)
                {
                    return to.Rank == promotionRank ? isPromotion : !isPromotion;
                }

                if (rankDelta == 2 * step && from.Rank == startRank && target == Piece.None)
                {
                    var middle = Square.From(from.File, from.Rank + step);
                    return GetPieceAt(state, middle) == Piece.None;
                }

                return false;
            }
            case PieceType.Knight:
                if (isCapture && target == Piece.None)
                {
                    return false;
                }

                return (Math.Abs(fileDelta), Math.Abs(rankDelta)) is (1, 2) or (2, 1);
            case PieceType.Bishop:
                if (isCapture && target == Piece.None)
                {
                    return false;
                }

                return Math.Abs(fileDelta) == Math.Abs(rankDelta) && IsPathClear(state, from, to);
            case PieceType.Rook:
                if (isCapture && target == Piece.None)
                {
                    return false;
                }

                return (fileDelta == 0 || rankDelta == 0) && IsPathClear(state, from, to);
            case PieceType.Queen:
            {
                if (isCapture && target == Piece.None)
                {
                    return false;
                }

                var diagonal = Math.Abs(fileDelta) == Math.Abs(rankDelta);
                var straight = fileDelta == 0 || rankDelta == 0;
                return (diagonal || straight) && IsPathClear(state, from, to);
            }
            case PieceType.King:
                if (isCapture && target == Piece.None)
                {
                    return false;
                }

                return Math.Max(Math.Abs(fileDelta), Math.Abs(rankDelta)) == 1;
            default:
                return false;
        }
    }

    private static bool IsPathClear(BoardState state, Square from, Square to)
    {
        var fileStep = Math.Sign(to.File - from.File);
        var rankStep = Math.Sign(to.Rank - from.Rank);

        var file = from.File + fileStep;
        var rank = from.Rank + rankStep;
        while (file != to.File || rank != to.Rank)
        {
            if (GetPieceAt(state, Square.From(file, rank)) != Piece.None)
            {
                return false;
            }

            file += fileStep;
            rank += rankStep;
        }

        return true;
    }

    private static bool IsLegalAfterMove(BoardState state, Square from, Square to, Piece mover, bool isCapture, PieceType promotion)
    {
        var snapshot = CloneState(state);
        ApplyMove(state, from, to, mover, isCapture, promotion);
        var movedWhite = IsWhite(mover);
        var legal = !IsKingInCheck(state, movedWhite);
        RestoreState(state, snapshot);
        return legal;
    }

    private static void ApplyMove(BoardState state, Square from, Square to, Piece mover, bool isCapture, PieceType promotion)
    {
        var movingWhite = IsWhite(mover);
        var target = GetPieceAt(state, to);
        var pawnMove = ToPieceType(mover) == PieceType.Pawn;

        if (pawnMove && isCapture && target == Piece.None && state.EnPassantSquare.HasValue && state.EnPassantSquare.Value.Index == to.Index)
        {
            var capturedRank = movingWhite ? to.Rank - 1 : to.Rank + 1;
            var capturedSquare = Square.From(to.File, capturedRank);
            var capturedPiece = GetPieceAt(state, capturedSquare);
            if (capturedPiece != Piece.None)
            {
                ClearPiece(state, capturedPiece, capturedSquare);
                target = capturedPiece;
            }
        }

        ClearPiece(state, mover, from);

        if (target != Piece.None)
        {
            ClearPiece(state, target, to);
        }

        var placedPiece = promotion == PieceType.None ? mover : PromotePiece(movingWhite, promotion);
        SetPiece(state, placedPiece, to);

        if (ToPieceType(mover) == PieceType.King)
        {
            var newRights = movingWhite
                ? state.CastlingRights.Remove((byte)(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide))
                : state.CastlingRights.Remove((byte)(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide));

            SetCastlingRights(state, newRights);
        }

        if (ToPieceType(mover) == PieceType.Rook)
        {
            RemoveRookCastlingRight(state, from.File, from.Rank);
        }

        if (target != Piece.None && ToPieceType(target) == PieceType.Rook)
        {
            RemoveRookCastlingRight(state, to.File, to.Rank);
        }

        SetEnPassantSquare(state, null);
        if (pawnMove && Math.Abs(to.Rank - from.Rank) == 2)
        {
            var epRank = movingWhite ? from.Rank + 1 : from.Rank - 1;
            SetEnPassantSquare(state, Square.From(to.File, epRank));
        }

        state.HalfMoveClock = pawnMove || target != Piece.None ? 0 : state.HalfMoveClock + 1;
        if (!movingWhite)
        {
            state.FullMoveNumber++;
        }

        ToggleSideToMove(state);
    }

    private static bool ApplyCastle(BoardState state, bool kingSide)
    {
        var white = state.SideToMove == Color.White;
        var rank = white ? 0 : 7;

        var kingFrom = Square.From(4, rank);
        var kingTo = Square.From(kingSide ? 6 : 2, rank);
        var rookFrom = Square.From(kingSide ? 7 : 0, rank);
        var rookTo = Square.From(kingSide ? 5 : 3, rank);

        var king = white ? Piece.WhiteKing : Piece.BlackKing;
        var rook = white ? Piece.WhiteRook : Piece.BlackRook;

        if (GetPieceAt(state, kingFrom) != king || GetPieceAt(state, rookFrom) != rook)
        {
            return false;
        }

        var requiredRight = white
            ? (kingSide ? CastlingRights.WhiteKingSide : CastlingRights.WhiteQueenSide)
            : (kingSide ? CastlingRights.BlackKingSide : CastlingRights.BlackQueenSide);

        if (!state.CastlingRights.Has(requiredRight))
        {
            return false;
        }

        var emptyFiles = kingSide ? new[] { 5, 6 } : new[] { 1, 2, 3 };
        foreach (var file in emptyFiles)
        {
            if (GetPieceAt(state, Square.From(file, rank)) != Piece.None)
            {
                return false;
            }
        }

        var kingPath = kingSide ? new[] { 4, 5, 6 } : new[] { 4, 3, 2 };
        foreach (var file in kingPath)
        {
            if (IsSquareAttacked(state, Square.From(file, rank), byWhite: !white))
            {
                return false;
            }
        }

        ClearPiece(state, king, kingFrom);
        ClearPiece(state, rook, rookFrom);
        SetPiece(state, king, kingTo);
        SetPiece(state, rook, rookTo);

        var newRights = white
            ? state.CastlingRights.Remove((byte)(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide))
            : state.CastlingRights.Remove((byte)(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide));
        SetCastlingRights(state, newRights);

        SetEnPassantSquare(state, null);
        state.HalfMoveClock++;
        if (!white)
        {
            state.FullMoveNumber++;
        }

        ToggleSideToMove(state);
        return true;
    }

    private static bool IsKingInCheck(BoardState state, bool whiteKing)
    {
        var kingPiece = whiteKing ? Piece.WhiteKing : Piece.BlackKing;
        var kingBoard = state.PieceBitboards[PieceToIndex(kingPiece)].Value;
        if (kingBoard == 0UL)
        {
            return true;
        }

        var kingSquare = new Square((byte)BitOperations.TrailingZeroCount(kingBoard));
        return IsSquareAttacked(state, kingSquare, byWhite: !whiteKing);
    }

    private static bool IsSquareAttacked(BoardState state, Square target, bool byWhite)
    {
        var pieces = byWhite
            ? new[] { Piece.WhitePawn, Piece.WhiteKnight, Piece.WhiteBishop, Piece.WhiteRook, Piece.WhiteQueen, Piece.WhiteKing }
            : new[] { Piece.BlackPawn, Piece.BlackKnight, Piece.BlackBishop, Piece.BlackRook, Piece.BlackQueen, Piece.BlackKing };

        foreach (var piece in pieces)
        {
            var bb = state.PieceBitboards[PieceToIndex(piece)].Value;
            while (bb != 0UL)
            {
                var fromIndex = BitOperations.TrailingZeroCount(bb);
                bb &= bb - 1;

                var from = new Square((byte)fromIndex);
                if (CanAttackSquare(state, from, target, piece))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CanAttackSquare(BoardState state, Square from, Square target, Piece piece)
    {
        var fileDelta = target.File - from.File;
        var rankDelta = target.Rank - from.Rank;

        switch (ToPieceType(piece))
        {
            case PieceType.Pawn:
            {
                var step = IsWhite(piece) ? 1 : -1;
                return rankDelta == step && Math.Abs(fileDelta) == 1;
            }
            case PieceType.Knight:
                return (Math.Abs(fileDelta), Math.Abs(rankDelta)) is (1, 2) or (2, 1);
            case PieceType.Bishop:
                return Math.Abs(fileDelta) == Math.Abs(rankDelta) && IsPathClear(state, from, target);
            case PieceType.Rook:
                return (fileDelta == 0 || rankDelta == 0) && IsPathClear(state, from, target);
            case PieceType.Queen:
            {
                var diagonal = Math.Abs(fileDelta) == Math.Abs(rankDelta);
                var straight = fileDelta == 0 || rankDelta == 0;
                return (diagonal || straight) && IsPathClear(state, from, target);
            }
            case PieceType.King:
                return Math.Max(Math.Abs(fileDelta), Math.Abs(rankDelta)) == 1;
            default:
                return false;
        }
    }

    private static Piece GetPieceAt(BoardState state, Square square)
    {
        var mask = 1UL << square.Index;
        for (var i = 0; i < BoardState.PieceBitboardCount; i++)
        {
            if ((state.PieceBitboards[i].Value & mask) != 0UL)
            {
                return (Piece)(i + 1);
            }
        }

        return Piece.None;
    }

    private static void SetPiece(BoardState state, Piece piece, Square square)
    {
        var index = PieceToIndex(piece);
        state.PieceBitboards[index] = state.PieceBitboards[index].With(square);
        state.ZobristKey ^= ZobristTables.PieceSquare[index, square.Index];

        var mask = new Bitboard(1UL << square.Index);
        if (IsWhite(piece))
        {
            state.WhiteOccupancy = state.WhiteOccupancy | mask;
        }
        else
        {
            state.BlackOccupancy = state.BlackOccupancy | mask;
        }
    }

    private static void ClearPiece(BoardState state, Piece piece, Square square)
    {
        var index = PieceToIndex(piece);
        state.PieceBitboards[index] = state.PieceBitboards[index].Without(square);
        state.ZobristKey ^= ZobristTables.PieceSquare[index, square.Index];

        var mask = new Bitboard(1UL << square.Index);
        if (IsWhite(piece))
        {
            state.WhiteOccupancy = new Bitboard(state.WhiteOccupancy.Value & ~mask.Value);
        }
        else
        {
            state.BlackOccupancy = new Bitboard(state.BlackOccupancy.Value & ~mask.Value);
        }
    }

    private static Piece PieceFor(Color sideToMove, char pieceLetter)
    {
        var white = sideToMove == Color.White;
        return (white, pieceLetter) switch
        {
            (true, 'K') => Piece.WhiteKing,
            (true, 'Q') => Piece.WhiteQueen,
            (true, 'R') => Piece.WhiteRook,
            (true, 'B') => Piece.WhiteBishop,
            (true, 'N') => Piece.WhiteKnight,
            (true, _) => Piece.WhitePawn,
            (false, 'K') => Piece.BlackKing,
            (false, 'Q') => Piece.BlackQueen,
            (false, 'R') => Piece.BlackRook,
            (false, 'B') => Piece.BlackBishop,
            (false, 'N') => Piece.BlackKnight,
            (false, _) => Piece.BlackPawn
        };
    }

    private static Piece PromotePiece(bool white, PieceType pieceType)
    {
        return (white, pieceType) switch
        {
            (true, PieceType.Knight) => Piece.WhiteKnight,
            (true, PieceType.Bishop) => Piece.WhiteBishop,
            (true, PieceType.Rook) => Piece.WhiteRook,
            (true, _) => Piece.WhiteQueen,
            (false, PieceType.Knight) => Piece.BlackKnight,
            (false, PieceType.Bishop) => Piece.BlackBishop,
            (false, PieceType.Rook) => Piece.BlackRook,
            (false, _) => Piece.BlackQueen
        };
    }

    private static PieceType ToPieceType(Piece piece)
    {
        return piece switch
        {
            Piece.WhitePawn or Piece.BlackPawn => PieceType.Pawn,
            Piece.WhiteKnight or Piece.BlackKnight => PieceType.Knight,
            Piece.WhiteBishop or Piece.BlackBishop => PieceType.Bishop,
            Piece.WhiteRook or Piece.BlackRook => PieceType.Rook,
            Piece.WhiteQueen or Piece.BlackQueen => PieceType.Queen,
            Piece.WhiteKing or Piece.BlackKing => PieceType.King,
            _ => PieceType.None
        };
    }

    private static bool IsWhite(Piece piece)
    {
        return piece is >= Piece.WhitePawn and <= Piece.WhiteKing;
    }

    private static int PieceToIndex(Piece piece)
    {
        return (int)piece - 1;
    }

    private static void RemoveRookCastlingRight(BoardState state, int file, int rank)
    {
        if (rank == 0 && file == 0)
        {
            SetCastlingRights(state, state.CastlingRights.Remove(CastlingRights.WhiteQueenSide));
        }
        else if (rank == 0 && file == 7)
        {
            SetCastlingRights(state, state.CastlingRights.Remove(CastlingRights.WhiteKingSide));
        }
        else if (rank == 7 && file == 0)
        {
            SetCastlingRights(state, state.CastlingRights.Remove(CastlingRights.BlackQueenSide));
        }
        else if (rank == 7 && file == 7)
        {
            SetCastlingRights(state, state.CastlingRights.Remove(CastlingRights.BlackKingSide));
        }
    }

    private static void SetCastlingRights(BoardState state, CastlingRights next)
    {
        var current = state.CastlingRights.Value & 0x0F;
        var updated = next.Value & 0x0F;
        if (current == updated)
        {
            return;
        }

        state.ZobristKey ^= ZobristTables.CastlingRights[current];
        state.ZobristKey ^= ZobristTables.CastlingRights[updated];
        state.CastlingRights = next;
    }

    private static void SetEnPassantSquare(BoardState state, Square? next)
    {
        if (state.EnPassantSquare.HasValue)
        {
            state.ZobristKey ^= ZobristTables.EnPassantFile[state.EnPassantSquare.Value.File];
        }

        state.EnPassantSquare = next;

        if (state.EnPassantSquare.HasValue)
        {
            state.ZobristKey ^= ZobristTables.EnPassantFile[state.EnPassantSquare.Value.File];
        }
    }

    private static void ToggleSideToMove(BoardState state)
    {
        state.ZobristKey ^= ZobristTables.SideToMove;
        state.SideToMove = state.SideToMove == Color.White ? Color.Black : Color.White;
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

    private static void RestoreState(BoardState target, BoardState source)
    {
        for (var i = 0; i < BoardState.PieceBitboardCount; i++)
        {
            target.PieceBitboards[i] = source.PieceBitboards[i];
        }

        target.WhiteOccupancy = source.WhiteOccupancy;
        target.BlackOccupancy = source.BlackOccupancy;
        target.SideToMove = source.SideToMove;
        target.CastlingRights = source.CastlingRights;
        target.EnPassantSquare = source.EnPassantSquare;
        target.HalfMoveClock = source.HalfMoveClock;
        target.FullMoveNumber = source.FullMoveNumber;
        target.ZobristKey = source.ZobristKey;
    }

    private static string NormalizeSan(string san)
    {
        var normalized = san.Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        while (normalized.Length > 0)
        {
            var tail = normalized[^1];
            if (tail is '+' or '#' or '!' or '?')
            {
                normalized = normalized[..^1];
                continue;
            }

            break;
        }

        return normalized.Replace("e.p.", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
    }
}
