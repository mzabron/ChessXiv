using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Models;
using ChessXiv.Domain.Engine.Types;

namespace ChessXiv.Application.Services;

public sealed class PositionPlayService(
    IBoardStateSerializer boardStateSerializer,
    IBoardStateTransition boardStateTransition) : IPositionPlayService
{
    public PositionMoveResponse TryApplyMove(PositionMoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Fen))
        {
            return Invalid("FEN is required.");
        }

        Square fromSquare;
        Square toSquare;

        var trimmedFen = request.Fen.Trim();

        BoardState state;
        try
        {
            state = boardStateSerializer.FromFen(trimmedFen);
        }
        catch (FormatException)
        {
            return Invalid("Invalid FEN.");
        }
        catch (ArgumentException)
        {
            return Invalid("Invalid FEN.");
        }

        var sanInput = request.San?.Trim();
        if (!string.IsNullOrWhiteSpace(sanInput))
        {
            if (!boardStateTransition.TryApplySan(state, sanInput))
            {
                return Invalid("Illegal move for current position.");
            }

            return new PositionMoveResponse
            {
                IsValid = true,
                Fen = boardStateSerializer.ToFen(state),
                San = sanInput
            };
        }

        if (!TryParseSquare(request.From ?? string.Empty, out fromSquare)
            || !TryParseSquare(request.To ?? string.Empty, out toSquare))
        {
            return Invalid("Move squares must be in algebraic coordinate format (for example: e2, e4). You can also send SAN (for example: Nf3).");
        }

        if (fromSquare.Index == toSquare.Index)
        {
            return Invalid("Source and destination squares must be different.");
        }

        PieceType? promotion = ParsePromotion(request.Promotion);
        if (!string.IsNullOrWhiteSpace(request.Promotion) && promotion is null)
        {
            return Invalid("Promotion must be one of: q, r, b, n.");
        }

        var movingPiece = GetPieceAt(state, fromSquare);
        if (movingPiece == Piece.None)
        {
            return Invalid("No piece found on source square.");
        }

        if (!BelongsToSideToMove(movingPiece, state.SideToMove))
        {
            return Invalid("Selected piece does not belong to side to move.");
        }

        if (ToPieceType(movingPiece) == PieceType.Pawn && (toSquare.Rank == 0 || toSquare.Rank == 7) && promotion is null)
        {
            promotion = PieceType.Queen;
        }

        foreach (var sanCandidate in BuildSanCandidates(movingPiece, fromSquare, toSquare, promotion))
        {
            BoardState candidate;
            try
            {
                candidate = boardStateSerializer.FromFen(trimmedFen);
            }
            catch
            {
                return Invalid("Invalid FEN.");
            }

            if (!boardStateTransition.TryApplySan(candidate, sanCandidate))
            {
                continue;
            }

            if (!MatchesRequestedMove(state, candidate, fromSquare, toSquare, movingPiece, promotion))
            {
                continue;
            }

            return new PositionMoveResponse
            {
                IsValid = true,
                Fen = boardStateSerializer.ToFen(candidate),
                San = sanCandidate
            };
        }

        return Invalid("Illegal move for current position.");
    }

    private static PositionMoveResponse Invalid(string error)
    {
        return new PositionMoveResponse
        {
            IsValid = false,
            Error = error
        };
    }

    private static IEnumerable<string> BuildSanCandidates(Piece movingPiece, Square from, Square to, PieceType? promotion)
    {
        var promotionSuffix = promotion.HasValue && promotion.Value != PieceType.None
            ? "=" + PromotionToSan(promotion.Value)
            : string.Empty;

        if (ToPieceType(movingPiece) == PieceType.King && from.File == 4)
        {
            if (from.Rank == 0 && to.Rank == 0)
            {
                if (to.File == 6)
                {
                    return new[] { "O-O", "0-0" };
                }

                if (to.File == 2)
                {
                    return new[] { "O-O-O", "0-0-0" };
                }
            }

            if (from.Rank == 7 && to.Rank == 7)
            {
                if (to.File == 6)
                {
                    return new[] { "O-O", "0-0" };
                }

                if (to.File == 2)
                {
                    return new[] { "O-O-O", "0-0-0" };
                }
            }
        }

        var destination = SquareToNotation(to);
        var fromFile = ((char)('a' + from.File)).ToString();
        var fromRank = ((char)('1' + from.Rank)).ToString();
        var fromSquare = SquareToNotation(from);

        if (ToPieceType(movingPiece) == PieceType.Pawn)
        {
            return new[]
            {
                destination + promotionSuffix,
                fromFile + "x" + destination + promotionSuffix
            };
        }

        var pieceLetter = PieceLetter(ToPieceType(movingPiece));
        var prefixes = new[]
        {
            pieceLetter,
            pieceLetter + fromFile,
            pieceLetter + fromRank,
            pieceLetter + fromSquare
        };

        var candidates = new List<string>(8);
        foreach (var prefix in prefixes)
        {
            candidates.Add(prefix + destination);
            candidates.Add(prefix + "x" + destination);
        }

        return candidates.Distinct(StringComparer.Ordinal);
    }

    private static bool MatchesRequestedMove(
        BoardState initial,
        BoardState result,
        Square from,
        Square to,
        Piece movingPiece,
        PieceType? promotion)
    {
        if (GetPieceAt(result, from) != Piece.None)
        {
            return false;
        }

        var expectedPiece = promotion.HasValue && promotion.Value != PieceType.None
            ? PromotePiece(IsWhite(movingPiece), promotion.Value)
            : movingPiece;

        if (GetPieceAt(result, to) != expectedPiece)
        {
            return false;
        }

        return initial.SideToMove != result.SideToMove;
    }

    private static bool TryParseSquare(string value, out Square square)
    {
        square = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 2)
        {
            return false;
        }

        var fileChar = normalized[0];
        var rankChar = normalized[1];
        if (fileChar is < 'a' or > 'h' || rankChar is < '1' or > '8')
        {
            return false;
        }

        square = Square.From(fileChar - 'a', rankChar - '1');
        return true;
    }

    private static PieceType? ParsePromotion(string? promotion)
    {
        if (string.IsNullOrWhiteSpace(promotion))
        {
            return null;
        }

        return promotion.Trim().ToLowerInvariant() switch
        {
            "q" => PieceType.Queen,
            "r" => PieceType.Rook,
            "b" => PieceType.Bishop,
            "n" => PieceType.Knight,
            _ => null
        };
    }

    private static string PromotionToSan(PieceType promotion)
    {
        return promotion switch
        {
            PieceType.Knight => "N",
            PieceType.Bishop => "B",
            PieceType.Rook => "R",
            _ => "Q"
        };
    }

    private static string PieceLetter(PieceType pieceType)
    {
        return pieceType switch
        {
            PieceType.King => "K",
            PieceType.Queen => "Q",
            PieceType.Rook => "R",
            PieceType.Bishop => "B",
            PieceType.Knight => "N",
            _ => string.Empty
        };
    }

    private static string SquareToNotation(Square square)
    {
        var file = (char)('a' + square.File);
        var rank = (char)('1' + square.Rank);
        return string.Concat(file, rank);
    }

    private static bool BelongsToSideToMove(Piece piece, Color sideToMove)
    {
        return sideToMove == Color.White ? IsWhite(piece) : !IsWhite(piece);
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

    private static Piece GetPieceAt(in BoardState state, Square square)
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

    private static bool IsWhite(Piece piece)
    {
        return piece is >= Piece.WhitePawn and <= Piece.WhiteKing;
    }
}
