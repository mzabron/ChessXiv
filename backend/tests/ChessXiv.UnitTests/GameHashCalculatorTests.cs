using ChessXiv.Application.Services;
using ChessXiv.Domain.Entities;

namespace ChessXiv.UnitTests;

public class GameHashCalculatorTests
{
    [Fact]
    public void Compute_DoesNotThrow_ForEmptyIncompleteGame()
    {
        var game = new Game
        {
            Id = Guid.NewGuid(),
            White = string.Empty,
            Black = string.Empty,
            Result = "*",
            Pgn = string.Empty,
            Moves = []
        };

        var hash = GameHashCalculator.Compute(game);

        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    public void Compute_NormalizesMoveWhitespace_AndProducesSameHash()
    {
        var gameA = CreateGame("Magnus Carlsen", "Hikaru Nakamura", " e4 ", " e5 ");
        var gameB = CreateGame("Magnus Carlsen", "Hikaru Nakamura", "e4", "e5");

        var hashA = GameHashCalculator.Compute(gameA);
        var hashB = GameHashCalculator.Compute(gameB);

        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void Compute_IsCaseInsensitive_ForSquareMoves()
    {
        var gameA = CreateGame("Magnus Carlsen", "Hikaru Nakamura", "e4", "e5");
        var gameB = CreateGame("Magnus Carlsen", "Hikaru Nakamura", "E4", "E5");

        var hashA = GameHashCalculator.Compute(gameA);
        var hashB = GameHashCalculator.Compute(gameB);

        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void Compute_IgnoresMoveAnnotations_WhenHashing()
    {
        var gameA = CreateGame("Magnus Carlsen", "Hikaru Nakamura", "Nf3+", "Nc6?!");
        var gameB = CreateGame("Magnus Carlsen", "Hikaru Nakamura", "Nf3", "Nc6");

        var hashA = GameHashCalculator.Compute(gameA);
        var hashB = GameHashCalculator.Compute(gameB);

        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void Compute_CanonicalizesCommaSeparatedPlayerNames()
    {
        var gameA = CreateGame("Magnus Carlsen", "Hikaru Nakamura", "e4", "e5");
        var gameB = CreateGame("Carlsen, Magnus", "Nakamura, Hikaru", "e4", "e5");

        var hashA = GameHashCalculator.Compute(gameA);
        var hashB = GameHashCalculator.Compute(gameB);

        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void Compute_DoesNotChange_WhenNonHashMetadataChanges()
    {
        var gameA = CreateGame("Magnus Carlsen", "Hikaru Nakamura", "e4", "e5", site: "Oslo", round: "1");
        var gameB = CreateGame("Magnus Carlsen", "Hikaru Nakamura", "e4", "e5", site: "Paris", round: "9");

        var hashA = GameHashCalculator.Compute(gameA);
        var hashB = GameHashCalculator.Compute(gameB);

        Assert.Equal(hashA, hashB);
    }

    private static Game CreateGame(string white, string black, string whiteMove, string blackMove, string? site = null, string? round = null)
    {
        return new Game
        {
            Id = Guid.NewGuid(),
            White = white,
            Black = black,
            Site = site,
            Round = round,
            Result = "*",
            Pgn = "dummy",
            Moves =
            [
                new Move
                {
                    Id = Guid.NewGuid(),
                    MoveNumber = 1,
                    WhiteMove = whiteMove,
                    BlackMove = blackMove
                }
            ]
        };
    }
}
