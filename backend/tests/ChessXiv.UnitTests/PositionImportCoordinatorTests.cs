using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Factories;
using ChessXiv.Domain.Engine.Serialization;
using ChessXiv.Domain.Engine.Services;
using ChessXiv.Domain.Entities;

namespace ChessXiv.UnitTests;

public class PositionImportCoordinatorTests
{
    private const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public async Task PopulateAsync_GeneratesPositions_ForEveryPly()
    {
        var coordinator = CreateCoordinator();
        var game = CreateGame([
            (1, "e4", "e5"),
            (2, "Nf3", "Nc6")
        ]);

        await coordinator.PopulateAsync([game]);

        Assert.Equal(5, game.Positions.Count);

        var ordered = game.Positions.OrderBy(p => p.PlyCount).ToList();
        Assert.Equal([0, 1, 2, 3, 4], ordered.Select(p => p.PlyCount).ToArray());
        Assert.Null(ordered[0].LastMove);
        Assert.Equal(InitialFen, ordered[0].Fen);
        Assert.Equal("e4", ordered[1].LastMove);
        Assert.Equal("e5", ordered[2].LastMove);
        Assert.Equal("Nf3", ordered[3].LastMove);
        Assert.Equal("Nc6", ordered[4].LastMove);

        Assert.Equal("r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3", ordered[^1].Fen);
    }

    [Fact]
    public async Task PopulateAsync_StopsReplay_WhenSanCannotBeApplied()
    {
        var coordinator = CreateCoordinator();
        var game = CreateGame([
            (1, "e4", "e5"),
            (2, "InvalidMove", "Nc6")
        ]);

        await coordinator.PopulateAsync([game]);

        var ordered = game.Positions.OrderBy(p => p.PlyCount).ToList();
        Assert.Equal(3, ordered.Count);
        Assert.Equal([0, 1, 2], ordered.Select(p => p.PlyCount).ToArray());
        Assert.Equal("e5", ordered[^1].LastMove);
    }

    [Fact]
    public async Task PopulateAsync_DoesNotLeakState_BetweenGamesInBatch()
    {
        var coordinator = CreateCoordinator();
        var gameOne = CreateGame([
            (1, "e4", "e5"),
            (2, "Nf3", "Nc6")
        ]);
        var gameTwo = CreateGame([
            (1, "e4", "e5"),
            (2, "Nf3", "Nc6")
        ]);

        await coordinator.PopulateAsync([gameOne, gameTwo]);

        var one = gameOne.Positions.OrderBy(p => p.PlyCount).Select(p => (p.PlyCount, p.Fen, p.LastMove, p.FenHash)).ToArray();
        var two = gameTwo.Positions.OrderBy(p => p.PlyCount).Select(p => (p.PlyCount, p.Fen, p.LastMove, p.FenHash)).ToArray();

        Assert.Equal(one, two);
    }

    [Fact]
    public async Task PopulateAsync_HandlesPromotionMove_FromSan()
    {
        var coordinator = CreateCoordinator();
        var game = CreateGame([
            (1, "a4", "h5"),
            (2, "a5", "h4"),
            (3, "a6", "h3"),
            (4, "axb7", "hxg2"),
            (5, "bxa8=Q", null)
        ]);

        await coordinator.PopulateAsync([game]);

        var last = game.Positions.OrderBy(p => p.PlyCount).Last();
        Assert.Equal("bxa8=Q", last.LastMove);
        Assert.Contains("Q", last.Fen, StringComparison.Ordinal);
    }

    private static PositionImportCoordinator CreateCoordinator()
    {
        var serializer = new FenBoardStateSerializer();
        var factory = new BoardStateFactory(serializer);
        var transition = new BitboardBoardStateTransition();
        var hasher = new ZobristPositionHasher();
        return new PositionImportCoordinator(factory, serializer, transition, hasher);
    }

    private static Game CreateGame(IReadOnlyCollection<(int MoveNumber, string White, string? Black)> plies)
    {
        var game = new Game
        {
            Id = Guid.NewGuid(),
            White = "White",
            Black = "Black",
            Result = "*",
            Pgn = "dummy"
        };

        foreach (var ply in plies)
        {
            game.Moves.Add(new Move
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                MoveNumber = ply.MoveNumber,
                WhiteMove = ply.White,
                BlackMove = ply.Black
            });
        }

        return game;
    }
}
