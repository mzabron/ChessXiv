using ChessBase.Domain.Engine.Serialization;
using ChessBase.Domain.Engine.Types;
using System.Numerics;

namespace ChessBase.UnitTests;

public class FenBoardStateSerializerTests
{
    private readonly FenBoardStateSerializer _serializer = new();

    [Fact]
    public void FromFen_ParsesStandardInitialPosition()
    {
        const string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        var state = _serializer.FromFen(fen);

        Assert.Equal(Color.White, state.SideToMove);
        Assert.True(state.CastlingRights.Has(CastlingRights.WhiteKingSide));
        Assert.True(state.CastlingRights.Has(CastlingRights.WhiteQueenSide));
        Assert.True(state.CastlingRights.Has(CastlingRights.BlackKingSide));
        Assert.True(state.CastlingRights.Has(CastlingRights.BlackQueenSide));
        Assert.Null(state.EnPassantSquare);
        Assert.Equal(0, state.HalfMoveClock);
        Assert.Equal(1, state.FullMoveNumber);

        Assert.True(state.PieceBitboards[(int)Piece.WhiteKing - 1].Contains(Square.From(4, 0)));
        Assert.True(state.PieceBitboards[(int)Piece.BlackKing - 1].Contains(Square.From(4, 7)));

        Assert.False(state.WhiteOccupancy.IsEmpty);
        Assert.False(state.BlackOccupancy.IsEmpty);
        Assert.Equal(32, BitOperations.PopCount(state.AllOccupancy.Value));
    }

    [Fact]
    public void ToFen_RoundTripsComplexPosition()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/1bBPP3/5N2/PPP2PPP/RNBQK2R b KQkq d3 2 4";

        var state = _serializer.FromFen(fen);
        var serialized = _serializer.ToFen(state);

        Assert.Equal(fen, serialized);
    }

    [Fact]
    public void FromFen_Throws_WhenFenFieldCountIsInvalid()
    {
        const string fen = "8/8/8/8/8/8/8/8 w - - 0";

        var exception = Assert.Throws<FormatException>(() => _serializer.FromFen(fen));

        Assert.Contains("6 space-separated fields", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromFen_Throws_WhenPlacementContainsUnsupportedPiece()
    {
        const string fen = "rnbqkbnx/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        var exception = Assert.Throws<FormatException>(() => _serializer.FromFen(fen));

        Assert.Contains("Unsupported piece character", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToFen_PreservesEnPassantField_ForSquareAndDash()
    {
        const string withEnPassant = "rnbqkbnr/pppppppp/8/8/3Pp3/8/PPP1PPPP/RNBQKBNR b KQkq d3 0 2";
        const string withoutEnPassant = "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR b KQkq - 0 1";

        var withEnPassantState = _serializer.FromFen(withEnPassant);
        var withoutEnPassantState = _serializer.FromFen(withoutEnPassant);

        var withEnPassantSerialized = _serializer.ToFen(withEnPassantState);
        var withoutEnPassantSerialized = _serializer.ToFen(withoutEnPassantState);

        Assert.Equal(withEnPassant, withEnPassantSerialized);
        Assert.Equal(withoutEnPassant, withoutEnPassantSerialized);
        Assert.Equal(Square.From(3, 2), withEnPassantState.EnPassantSquare);
        Assert.Null(withoutEnPassantState.EnPassantSquare);
    }

    [Theory]
    [InlineData("KQ")]
    [InlineData("kq")]
    [InlineData("KQkq")]
    [InlineData("-")]
    public void ToFen_PreservesCastlingRightsVariants(string castling)
    {
        var fen = $"r3k2r/8/8/8/8/8/8/R3K2R w {castling} - 0 1";

        var state = _serializer.FromFen(fen);
        var serialized = _serializer.ToFen(state);

        Assert.Equal(fen, serialized);
    }

}
