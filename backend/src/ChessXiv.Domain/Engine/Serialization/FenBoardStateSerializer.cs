using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Models;
using ChessXiv.Domain.Engine.Types;
using System.Text;

namespace ChessXiv.Domain.Engine.Serialization;

public sealed class FenBoardStateSerializer : IBoardStateSerializer
{
    private const int FenPartsCount = 6;

    public BoardState FromFen(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            throw new ArgumentException("FEN cannot be null or empty.", nameof(fen));
        }

        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != FenPartsCount)
        {
            throw new FormatException("FEN must have 6 space-separated fields.");
        }

        var state = new BoardState
        {
            SideToMove = ParseSideToMove(parts[1]),
            CastlingRights = ParseCastlingRights(parts[2]),
            EnPassantSquare = ParseEnPassantSquare(parts[3]),
            HalfMoveClock = ParseInteger(parts[4], "halfmove clock"),
            FullMoveNumber = ParseInteger(parts[5], "fullmove number")
        };

        ParsePiecePlacement(parts[0], state);
        RecomputeOccupancy(state);

        return state;
    }

    public string ToFen(in BoardState state)
    {
        var board = BuildPieceLookup(state);
        var sb = new StringBuilder(96);

        for (var rank = 7; rank >= 0; rank--)
        {
            var emptyCount = 0;
            for (var file = 0; file < 8; file++)
            {
                var square = Square.From(file, rank);
                if (!board.TryGetValue(square.Index, out var piece))
                {
                    emptyCount++;
                    continue;
                }

                if (emptyCount > 0)
                {
                    sb.Append(emptyCount);
                    emptyCount = 0;
                }

                sb.Append(PieceToFenChar(piece));
            }

            if (emptyCount > 0)
            {
                sb.Append(emptyCount);
            }

            if (rank > 0)
            {
                sb.Append('/');
            }
        }

        sb.Append(' ');
        sb.Append(state.SideToMove == Color.White ? 'w' : 'b');
        sb.Append(' ');
        sb.Append(CastlingRightsToFen(state.CastlingRights));
        sb.Append(' ');
        sb.Append(EnPassantToFen(state.EnPassantSquare));
        sb.Append(' ');
        sb.Append(state.HalfMoveClock);
        sb.Append(' ');
        sb.Append(state.FullMoveNumber);

        return sb.ToString();
    }

    private static void ParsePiecePlacement(string placement, BoardState state)
    {
        var ranks = placement.Split('/');
        if (ranks.Length != 8)
        {
            throw new FormatException("Piece placement must contain 8 ranks.");
        }

        for (var fenRank = 0; fenRank < 8; fenRank++)
        {
            var rank = 7 - fenRank;
            var file = 0;

            foreach (var ch in ranks[fenRank])
            {
                if (char.IsDigit(ch))
                {
                    file += ch - '0';
                    continue;
                }

                var piece = FenCharToPiece(ch);
                if (piece == Piece.None)
                {
                    throw new FormatException($"Unsupported piece character '{ch}'.");
                }

                if (file is < 0 or > 7)
                {
                    throw new FormatException("Rank exceeds 8 files.");
                }

                var square = Square.From(file, rank);
                SetPiece(state, piece, square);
                file++;
            }

            if (file != 8)
            {
                throw new FormatException("Each rank must resolve to exactly 8 files.");
            }
        }
    }

    private static void SetPiece(BoardState state, Piece piece, Square square)
    {
        var index = PieceToBitboardIndex(piece);
        state.PieceBitboards[index] = state.PieceBitboards[index].With(square);
    }

    private static Dictionary<byte, Piece> BuildPieceLookup(in BoardState state)
    {
        var board = new Dictionary<byte, Piece>(32);

        for (var i = 0; i < BoardState.PieceBitboardCount; i++)
        {
            var piece = BitboardIndexToPiece(i);
            var bb = state.PieceBitboards[i].Value;

            for (byte sq = 0; sq < 64; sq++)
            {
                if ((bb & (1UL << sq)) == 0UL)
                {
                    continue;
                }

                board[sq] = piece;
            }
        }

        return board;
    }

    private static void RecomputeOccupancy(BoardState state)
    {
        Bitboard white = new(0UL);
        Bitboard black = new(0UL);

        for (var i = 0; i < BoardState.PieceBitboardCount; i++)
        {
            var piece = BitboardIndexToPiece(i);
            if (IsWhitePiece(piece))
            {
                white |= state.PieceBitboards[i];
                continue;
            }

            black |= state.PieceBitboards[i];
        }

        state.WhiteOccupancy = white;
        state.BlackOccupancy = black;
    }

    private static Color ParseSideToMove(string token)
    {
        return token switch
        {
            "w" => Color.White,
            "b" => Color.Black,
            _ => throw new FormatException("Side-to-move field must be 'w' or 'b'.")
        };
    }

    private static CastlingRights ParseCastlingRights(string token)
    {
        if (token == "-")
        {
            return CastlingRights.None;
        }

        var rights = CastlingRights.None;
        foreach (var ch in token)
        {
            rights = ch switch
            {
                'K' => rights.Add(CastlingRights.WhiteKingSide),
                'Q' => rights.Add(CastlingRights.WhiteQueenSide),
                'k' => rights.Add(CastlingRights.BlackKingSide),
                'q' => rights.Add(CastlingRights.BlackQueenSide),
                _ => throw new FormatException($"Invalid castling rights character '{ch}'.")
            };
        }

        return rights;
    }

    private static string CastlingRightsToFen(CastlingRights rights)
    {
        var sb = new StringBuilder(4);

        if (rights.Has(CastlingRights.WhiteKingSide)) sb.Append('K');
        if (rights.Has(CastlingRights.WhiteQueenSide)) sb.Append('Q');
        if (rights.Has(CastlingRights.BlackKingSide)) sb.Append('k');
        if (rights.Has(CastlingRights.BlackQueenSide)) sb.Append('q');

        return sb.Length == 0 ? "-" : sb.ToString();
    }

    private static Square? ParseEnPassantSquare(string token)
    {
        if (token == "-")
        {
            return null;
        }

        if (token.Length != 2)
        {
            throw new FormatException("En-passant field must be '-' or a square like 'e3'.");
        }

        var fileChar = token[0];
        var rankChar = token[1];

        if (fileChar is < 'a' or > 'h' || rankChar is < '1' or > '8')
        {
            throw new FormatException("En-passant square is out of range.");
        }

        var file = fileChar - 'a';
        var rank = rankChar - '1';
        return Square.From(file, rank);
    }

    private static string EnPassantToFen(Square? square)
    {
        if (!square.HasValue)
        {
            return "-";
        }

        var fileChar = (char)('a' + square.Value.File);
        var rankChar = (char)('1' + square.Value.Rank);
        return string.Concat(fileChar, rankChar);
    }

    private static int ParseInteger(string token, string fieldName)
    {
        if (!int.TryParse(token, out var value))
        {
            throw new FormatException($"Invalid {fieldName} value '{token}'.");
        }

        if (value < 0)
        {
            throw new FormatException($"{fieldName} cannot be negative.");
        }

        return value;
    }

    private static Piece FenCharToPiece(char ch)
    {
        return ch switch
        {
            'P' => Piece.WhitePawn,
            'N' => Piece.WhiteKnight,
            'B' => Piece.WhiteBishop,
            'R' => Piece.WhiteRook,
            'Q' => Piece.WhiteQueen,
            'K' => Piece.WhiteKing,
            'p' => Piece.BlackPawn,
            'n' => Piece.BlackKnight,
            'b' => Piece.BlackBishop,
            'r' => Piece.BlackRook,
            'q' => Piece.BlackQueen,
            'k' => Piece.BlackKing,
            _ => Piece.None
        };
    }

    private static char PieceToFenChar(Piece piece)
    {
        return piece switch
        {
            Piece.WhitePawn => 'P',
            Piece.WhiteKnight => 'N',
            Piece.WhiteBishop => 'B',
            Piece.WhiteRook => 'R',
            Piece.WhiteQueen => 'Q',
            Piece.WhiteKing => 'K',
            Piece.BlackPawn => 'p',
            Piece.BlackKnight => 'n',
            Piece.BlackBishop => 'b',
            Piece.BlackRook => 'r',
            Piece.BlackQueen => 'q',
            Piece.BlackKing => 'k',
            _ => throw new ArgumentOutOfRangeException(nameof(piece), piece, "Unsupported piece value.")
        };
    }

    private static int PieceToBitboardIndex(Piece piece)
    {
        if (piece is Piece.None)
        {
            throw new ArgumentOutOfRangeException(nameof(piece), piece, "Cannot map Piece.None.");
        }

        return (int)piece - 1;
    }

    private static Piece BitboardIndexToPiece(int index)
    {
        if (index is < 0 or >= BoardState.PieceBitboardCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Invalid bitboard index.");
        }

        return (Piece)(index + 1);
    }

    private static bool IsWhitePiece(Piece piece)
    {
        return piece is >= Piece.WhitePawn and <= Piece.WhiteKing;
    }
}
