using ChessXiv.Application.Services;

namespace ChessXiv.UnitTests;

public class PgnServiceMoveTests
{
    private readonly PgnService _service = new();

    [Fact]
    public void ParsePgn_ParsesMoveList_AndSkipsTrailingResultToken()
    {
        var pgn = PgnServiceTestData.LoadGamesSamplePgn();

        var firstGame = _service.ParsePgn(pgn).First();
        var firstMove = firstGame.Moves.First();
        var lastMove = firstGame.Moves.Last();

        Assert.True(firstGame.Moves.Count > 50);
        Assert.Equal(1, firstMove.MoveNumber);
        Assert.Equal("Nf3", firstMove.WhiteMove);
        Assert.Equal("g6", firstMove.BlackMove);

        Assert.Equal(63, lastMove.MoveNumber);
        Assert.Equal("Kg6", lastMove.WhiteMove);
        Assert.Null(lastMove.BlackMove);
        Assert.DoesNotContain(firstGame.Moves, move => move.WhiteMove == "1-0" || move.BlackMove == "1-0");
    }

    [Fact]
    public void ParsePgn_ParsesEvalAndClock_ForWhiteAndBlackMoves()
    {
        var pgn = PgnServiceTestData.LoadGamesSamplePgn();

        var firstGame = _service.ParsePgn(pgn).First();
        var firstMove = firstGame.Moves.Single(move => move.MoveNumber == 1);
        var secondMove = firstGame.Moves.Single(move => move.MoveNumber == 2);

        Assert.Equal(0.1, firstMove.WhiteEval);
        Assert.Equal(0.38, firstMove.BlackEval);
        Assert.Equal("0:10:02", firstMove.WhiteClk);
        Assert.Equal("0:10:03", firstMove.BlackClk);

        Assert.Equal(0.16, secondMove.WhiteEval);
        Assert.Equal(0.15, secondMove.BlackEval);
        Assert.Equal("0:10:03", secondMove.WhiteClk);
        Assert.Equal("0:10:04", secondMove.BlackClk);
    }

    [Fact]
    public void ParsePgn_ParsesBlackMoves_WhenNotationUsesThreeDots()
    {
        var pgn = PgnServiceTestData.LoadGamesSamplePgn();

        var firstGame = _service.ParsePgn(pgn).First();
        var firstMove = firstGame.Moves.Single(move => move.MoveNumber == 1);

        Assert.Equal("Nf3", firstMove.WhiteMove);
        Assert.Equal("g6", firstMove.BlackMove);
    }
}
