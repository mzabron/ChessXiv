using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Factories;
using ChessXiv.Domain.Engine.Models;
using ChessXiv.Domain.Engine.Serialization;
using ChessXiv.Domain.Engine.Services;

namespace ChessXiv.UnitTests;

public class EnPassantRulesTests
{
    private readonly FenBoardStateSerializer _serializer = new();
    private readonly IBoardStateTransition _transition = new BitboardBoardStateTransition();

    [Theory]
    // A double push with no enemy pawn beside it leaves no en-passant target.
    [InlineData(new[] { "e4" }, "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1")]
    [InlineData(new[] { "e4", "e5", "Nf3", "Nc6" }, "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3")]
    [InlineData(new[] { "Nf3", "Nc6", "e4", "e5" }, "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3")]
    public void DoublePush_WithoutAnAdjacentEnemyPawn_RecordsNoEnPassantSquare(string[] sans, string expectedFen)
    {
        var state = ReplayFrom(sans);

        Assert.Equal(expectedFen, _serializer.ToFen(state));
    }

    [Fact]
    public void DoublePush_WithAnAdjacentEnemyPawn_RecordsTheEnPassantSquare()
    {
        // After 1.e4 d5 2.e5 f5 the white e5 pawn stands beside f5, so the capture is on.
        var state = ReplayFrom(["e4", "d5", "e5", "f5"]);

        Assert.EndsWith("w KQkq f6 0 3", _serializer.ToFen(state), StringComparison.Ordinal);
    }

    [Fact]
    public void Transposition_ProducesIdenticalPositionKeys()
    {
        // The two orders differ only in the halfmove clock, which is not part of a position.
        var direct = ReplayFrom(["e4", "e5", "Nf3", "Nc6"]);
        var transposed = ReplayFrom(["Nf3", "Nc6", "e4", "e5"]);

        var calculator = new ZobristPositionKeyCalculator();

        Assert.Equal(
            Convert.ToHexString(calculator.Compute(direct)),
            Convert.ToHexString(calculator.Compute(transposed)));
    }

    [Fact]
    public void ReadingAFen_DropsAnEnPassantSquareNothingCanCaptureOn()
    {
        // Same placement, one FEN carries a stale en-passant square.
        var withStaleTarget = _serializer.FromFen("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1");
        var withoutTarget = _serializer.FromFen("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1");

        var calculator = new ZobristPositionKeyCalculator();

        Assert.Null(withStaleTarget.EnPassantSquare);
        Assert.Equal(
            Convert.ToHexString(calculator.Compute(withoutTarget)),
            Convert.ToHexString(calculator.Compute(withStaleTarget)));
    }

    [Fact]
    public void ReadingAFen_KeepsAnEnPassantSquareThatIsRealAndAffectsTheKey()
    {
        const string placement = "rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq";

        var capturable = _serializer.FromFen($"{placement} f6 0 3");
        var notCapturable = _serializer.FromFen($"{placement} - 0 3");

        var calculator = new ZobristPositionKeyCalculator();

        Assert.NotNull(capturable.EnPassantSquare);
        Assert.NotEqual(
            Convert.ToHexString(calculator.Compute(notCapturable)),
            Convert.ToHexString(calculator.Compute(capturable)));
    }

    [Fact]
    public void CastlingRights_AreNotPartOfPlacementButDoChangeTheKey()
    {
        const string placement = "r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w";

        var calculator = new ZobristPositionKeyCalculator();
        var allRights = calculator.Compute(_serializer.FromFen($"{placement} KQkq - 0 1"));
        var noRights = calculator.Compute(_serializer.FromFen($"{placement} - - 0 1"));

        Assert.NotEqual(Convert.ToHexString(allRights), Convert.ToHexString(noRights));
    }

    private BoardState ReplayFrom(string[] sans)
    {
        var state = new BoardStateFactory(_serializer).CreateInitial();

        foreach (var san in sans)
        {
            Assert.True(_transition.TryApplySan(state, san), $"Could not apply {san}.");
        }

        return state;
    }
}
