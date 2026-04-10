using ChessXiv.Domain.Engine.Factories;
using ChessXiv.Domain.Engine.Serialization;
using ChessXiv.Domain.Engine.Services;
using ChessXiv.Domain.Engine.Types;

namespace ChessXiv.UnitTests;

public class BitboardBoardStateTransitionTests
{
    [Fact]
    public void TryApplySan_PromotionCapture_UpdatesBitboardsAndOccupancy()
    {
        var serializer = new FenBoardStateSerializer();
        var transition = new BitboardBoardStateTransition();
        var state = serializer.FromFen("1r2k3/P7/8/8/8/8/8/4K3 w - - 0 1");
        var a7 = Square.From(0, 6);
        var b8 = Square.From(1, 7);

        var ok = transition.TryApplySan(state, "axb8=N");

        Assert.True(ok);
        Assert.False(state.PieceBitboards[(int)Piece.WhitePawn - 1].Contains(a7));
        Assert.True(state.PieceBitboards[(int)Piece.WhiteKnight - 1].Contains(b8));
        Assert.False(state.BlackOccupancy.Contains(b8));
    }

    [Fact]
    public void TryApplySan_EnPassantSquare_ClearsOnNextNonPawnDoubleMove()
    {
        var serializer = new FenBoardStateSerializer();
        var factory = new BoardStateFactory(serializer);
        var transition = new BitboardBoardStateTransition();
        var state = factory.CreateInitial();

        Assert.True(transition.TryApplySan(state, "e4"));
        Assert.Equal(Square.From(4, 2), state.EnPassantSquare);

        Assert.True(transition.TryApplySan(state, "Nf6"));
        Assert.Null(state.EnPassantSquare);
    }

    [Fact]
    public void TryApplySan_CastleThroughCheck_IsRejected()
    {
        var serializer = new FenBoardStateSerializer();
        var transition = new BitboardBoardStateTransition();
        var state = serializer.FromFen("4k3/8/8/8/2b5/8/8/4K2R w K - 0 1");

        var ok = transition.TryApplySan(state, "O-O");

        Assert.False(ok);
    }

    [Fact]
    public void ZobristKey_DoesNotDrift_AfterMoveSequence()
    {
        var serializer = new FenBoardStateSerializer();
        var factory = new BoardStateFactory(serializer);
        var transition = new BitboardBoardStateTransition();
        var hasher = new ZobristPositionHasher();
        var state = factory.CreateInitial();
        state.ZobristKey = hasher.Compute(state);

        var sequence = new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4", "Nf6", "O-O", "Be7" };

        foreach (var san in sequence)
        {
            Assert.True(transition.TryApplySan(state, san));
        }

        var fen = serializer.ToFen(state);
        var reconstructed = serializer.FromFen(fen);
        var recomputed = hasher.Compute(reconstructed);

        Assert.Equal(recomputed, state.ZobristKey);
    }
}
