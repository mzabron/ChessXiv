using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Factories;
using ChessXiv.Domain.Engine.Models;
using ChessXiv.Domain.Engine.Serialization;
using ChessXiv.Domain.Engine.Services;
using ChessXiv.Domain.Entities;

namespace ChessXiv.UnitTests;

public class PositionImportCoordinatorTests
{
    [Fact]
    public async Task PopulateAsync_GeneratesPositions_ForEveryPly()
    {
        var coordinator = CreateCoordinator();
        var parsed = CreateGame("1-0", [
            (1, "e4", "e5"),
            (2, "Nf3", "Nc6")
        ]);

        await coordinator.PopulateAsync([parsed]);

        var ordered = parsed.Game.Positions.OrderBy(p => p.PlyCount).ToList();
        Assert.Equal(5, ordered.Count);
        Assert.Equal<short[]>([0, 1, 2, 3, 4], ordered.Select(p => p.PlyCount).ToArray());
    }

    [Fact]
    public async Task PopulateAsync_RecordsTheMovePlayedFromEachPosition()
    {
        // NextMove is what makes the opening tree a single index scan: the continuations of
        // a position are stored on that position's own row.
        var coordinator = CreateCoordinator();
        var parsed = CreateGame("1-0", [
            (1, "e4", "e5"),
            (2, "Nf3", "Nc6")
        ]);

        await coordinator.PopulateAsync([parsed]);

        var ordered = parsed.Game.Positions.OrderBy(p => p.PlyCount).ToList();
        Assert.Equal("e4", ordered[0].NextMove);
        Assert.Equal("e5", ordered[1].NextMove);
        Assert.Equal("Nf3", ordered[2].NextMove);
        Assert.Equal("Nc6", ordered[3].NextMove);
        Assert.Null(ordered[4].NextMove);
    }

    [Fact]
    public async Task PopulateAsync_DenormalizesGameResultOntoEveryPosition()
    {
        var coordinator = CreateCoordinator();
        var parsed = CreateGame("0-1", [(1, "e4", "e5")]);

        await coordinator.PopulateAsync([parsed]);

        Assert.All(parsed.Game.Positions, p => Assert.Equal(GameResult.BlackWin, p.Result));
    }

    [Fact]
    public async Task PopulateAsync_StopsReplay_WhenSanCannotBeApplied()
    {
        var coordinator = CreateCoordinator();
        var parsed = CreateGame("*", [
            (1, "e4", "e5"),
            (2, "InvalidMove", "Nc6")
        ]);

        await coordinator.PopulateAsync([parsed]);

        var ordered = parsed.Game.Positions.OrderBy(p => p.PlyCount).ToList();
        Assert.Equal(3, ordered.Count);
        // The unplayable move must not be recorded as a continuation.
        Assert.Null(ordered[^1].NextMove);
    }

    [Fact]
    public async Task PopulateAsync_ProducesIdenticalKeys_ForIdenticalGamesInOneBatch()
    {
        var coordinator = CreateCoordinator();
        var one = CreateGame("1-0", [(1, "e4", "e5"), (2, "Nf3", "Nc6")]);
        var two = CreateGame("1-0", [(1, "e4", "e5"), (2, "Nf3", "Nc6")]);

        await coordinator.PopulateAsync([one, two]);

        var keysOne = one.Game.Positions.OrderBy(p => p.PlyCount).Select(p => Convert.ToHexString(p.PosKey)).ToArray();
        var keysTwo = two.Game.Positions.OrderBy(p => p.PlyCount).Select(p => Convert.ToHexString(p.PosKey)).ToArray();

        Assert.Equal(keysOne, keysTwo);
    }

    [Fact]
    public async Task PopulateAsync_ProducesTheSameKey_ForATransposition()
    {
        // The whole point of keying on position identity rather than on a FEN string.
        // These two orders reach the same placement and differ only in the halfmove clock
        // (2 vs 0), which is not part of a position.
        var coordinator = CreateCoordinator();
        var direct = CreateGame("*", [(1, "e4", "e5"), (2, "Nf3", "Nc6")]);
        var transposed = CreateGame("*", [(1, "Nf3", "Nc6"), (2, "e4", "e5")]);

        await coordinator.PopulateAsync([direct, transposed]);

        var directFinal = direct.Game.Positions.OrderBy(p => p.PlyCount).Last();
        var transposedFinal = transposed.Game.Positions.OrderBy(p => p.PlyCount).Last();

        Assert.Equal(Convert.ToHexString(directFinal.PosKey), Convert.ToHexString(transposedFinal.PosKey));
    }

    [Fact]
    public async Task PopulateAsync_DistinguishesDifferentPositions()
    {
        var coordinator = CreateCoordinator();
        var parsed = CreateGame("*", [(1, "e4", "e5"), (2, "Nf3", "Nc6")]);

        await coordinator.PopulateAsync([parsed]);

        var distinctKeys = parsed.Game.Positions
            .Select(p => Convert.ToHexString(p.PosKey))
            .Distinct()
            .Count();

        Assert.Equal(parsed.Game.Positions.Count, distinctKeys);
    }

    [Fact]
    public async Task PopulateAsync_HandlesPromotionMove_FromSan()
    {
        var coordinator = CreateCoordinator();
        var parsed = CreateGame("1-0", [
            (1, "a4", "h5"),
            (2, "a5", "h4"),
            (3, "a6", "h3"),
            (4, "axb7", "hxg2"),
            (5, "bxa8=Q", null)
        ]);

        await coordinator.PopulateAsync([parsed]);

        var ordered = parsed.Game.Positions.OrderBy(p => p.PlyCount).ToList();
        Assert.Equal("bxa8=Q", ordered[^2].NextMove);

        var serializer = new FenBoardStateSerializer();
        var finalFen = ReplayToFen(serializer, parsed);
        Assert.Contains("Q", finalFen.Split(' ')[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Batches at or above the parallel threshold take a different code path, so the
    /// per-game isolation of the replay state is worth asserting there too.
    /// </summary>
    [Fact]
    public async Task PopulateAsync_IsCorrect_ForBatchesLargeEnoughToRunInParallel()
    {
        var coordinator = CreateCoordinator();
        var games = Enumerable.Range(0, 64)
            .Select(_ => CreateGame("1-0", [(1, "e4", "e5"), (2, "Nf3", "Nc6")]))
            .ToArray();

        await coordinator.PopulateAsync(games);

        var expected = games[0].Game.Positions
            .OrderBy(p => p.PlyCount)
            .Select(p => (p.PlyCount, Key: Convert.ToHexString(p.PosKey), p.NextMove))
            .ToArray();

        Assert.All(games, parsed =>
        {
            var actual = parsed.Game.Positions
                .OrderBy(p => p.PlyCount)
                .Select(p => (p.PlyCount, Key: Convert.ToHexString(p.PosKey), p.NextMove))
                .ToArray();

            Assert.Equal(expected, actual);
        });
    }

    private static PositionImportCoordinator CreateCoordinator()
    {
        var serializer = new FenBoardStateSerializer();
        var factory = new BoardStateFactory(serializer);
        var transition = new BitboardBoardStateTransition();
        return new PositionImportCoordinator(factory, transition, new ZobristPositionKeyCalculator());
    }

    private static string ReplayToFen(IBoardStateSerializer serializer, ParsedGame parsed)
    {
        var state = new BoardStateFactory(serializer).CreateInitial();
        var transition = new BitboardBoardStateTransition();

        foreach (var move in parsed.Moves)
        {
            if (!string.IsNullOrWhiteSpace(move.WhiteMove) && !transition.TryApplySan(state, move.WhiteMove))
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(move.BlackMove) && !transition.TryApplySan(state, move.BlackMove!))
            {
                break;
            }
        }

        return serializer.ToFen(state);
    }

    private static ParsedGame CreateGame(
        string result,
        IReadOnlyCollection<(int MoveNumber, string White, string? Black)> plies)
    {
        return new ParsedGame
        {
            Game = new Game
            {
                Id = Guid.NewGuid(),
                White = "White",
                Black = "Black",
                Result = result,
                Pgn = "dummy"
            },
            Moves = plies
                .Select(ply => new ParsedMove
                {
                    MoveNumber = ply.MoveNumber,
                    WhiteMove = ply.White,
                    BlackMove = ply.Black
                })
                .ToArray()
        };
    }
}
