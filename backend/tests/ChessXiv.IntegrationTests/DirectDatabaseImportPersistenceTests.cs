using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Factories;
using ChessXiv.Domain.Engine.Serialization;
using ChessXiv.Domain.Engine.Services;
using ChessXiv.Domain.Entities;
using ChessXiv.IntegrationTests.Infrastructure;
using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class DirectDatabaseImportPersistenceTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task ImportToDatabaseAsync_PersistsGamesPositionsAndLinks()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, userDatabaseId) = await CreateOwnerAndDatabaseAsync(dbContext, "import-user");
        var importService = CreateImportService(dbContext);

        const string pgn = """
            [Event "Integration Import"]
            [Site "Test"]
            [Date "2026.03.04"]
            [Round "1"]
            [White "Alpha"]
            [Black "Beta"]
            [Result "1-0"]

            1. e4 { [%eval 0.18] [%clk 0:10:00] } 1... c5 { [%eval 0.25] [%clk 0:09:58] } 2. Nf3 d6 1-0
            """;

        using var reader = new StringReader(pgn);
        var result = await importService.ImportToDatabaseAsync(reader, ownerId, userDatabaseId);

        Assert.Equal(1, result.ParsedCount);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);

        Assert.Equal(1, await dbContext.Games.CountAsync());
        Assert.Equal(1, await dbContext.UserDatabaseGames.CountAsync());
        // One row per ply, plus the starting position.
        Assert.Equal(5, await dbContext.Positions.CountAsync());

        var savedGame = await dbContext.Games.Include(g => g.Positions).SingleAsync();
        Assert.Equal("Alpha", savedGame.White);
        Assert.Equal("Beta", savedGame.Black);
        Assert.Equal("1-0", savedGame.Result);
        Assert.Equal(4, savedGame.Positions.Max(p => p.PlyCount));

        // Every position carries the continuation and the game's result, which is what the
        // opening tree reads without joining back to Games.
        var ordered = savedGame.Positions.OrderBy(p => p.PlyCount).ToList();
        Assert.Equal("e4", ordered[0].NextMove);
        Assert.Equal("c5", ordered[1].NextMove);
        Assert.Null(ordered[^1].NextMove);
        Assert.All(ordered, p => Assert.Equal(GameResult.WhiteWin, p.Result));
        Assert.All(ordered, p => Assert.Equal(16, p.PosKey.Length));

        // The denormalised counter must match reality, since the panel renders it.
        var userDatabase = await dbContext.UserDatabases.SingleAsync(d => d.Id == userDatabaseId);
        Assert.Equal(1, userDatabase.GameCount);
    }

    [Fact]
    public async Task ImportToDatabaseAsync_PersistsGame_WhenLastBlackMoveIsMissing()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, userDatabaseId) = await CreateOwnerAndDatabaseAsync(dbContext, "partial-move-user");
        var importService = CreateImportService(dbContext);

        const string pgn = """
            [Event "Partial Move Import"]
            [Site "Test"]
            [Date "2026.03.04"]
            [Round "1"]
            [White "Gamma"]
            [Black "Delta"]
            [Result "*"]

            1. e4 e5 2. Nf3
            """;

        using var reader = new StringReader(pgn);
        var result = await importService.ImportToDatabaseAsync(reader, ownerId, userDatabaseId);

        Assert.Equal(1, result.ImportedCount);

        var savedGame = await dbContext.Games.Include(g => g.Positions).SingleAsync();
        Assert.Equal(3, savedGame.Positions.Max(p => p.PlyCount));
    }

    [Fact]
    public async Task PositionSearch_FindsTranspositions_ButExactPlyPinsTheMoveNumber()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, userDatabaseId) = await CreateOwnerAndDatabaseAsync(dbContext, "position-search-user");
        var importService = CreateImportService(dbContext);

        // Same final placement, reached in a different order. They differ only in the
        // halfmove clock, which is not part of a position.
        foreach (var pgn in new[]
        {
            """
            [White "Direct"]
            [Black "Order"]
            [Result "1-0"]

            1. e4 e5 2. Nf3 Nc6 1-0
            """,
            """
            [White "Transposed"]
            [Black "Order"]
            [Result "0-1"]

            1. Nf3 Nc6 2. e4 e5 0-1
            """
        })
        {
            using var reader = new StringReader(pgn);
            await importService.ImportToDatabaseAsync(reader, ownerId, userDatabaseId);
        }

        dbContext.ChangeTracker.Clear();

        const string reachedFen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";
        var target = PositionSearchTarget.Resolve(
            true,
            reachedFen,
            new FenBoardStateSerializer(),
            new ZobristPositionKeyCalculator())!;

        Assert.Equal(4, target.PlyCount);

        var anyOrder = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabaseId == userDatabaseId)
            .ApplyPositionFilters(true, target.PosKey)
            .CountAsync();

        var atThisPly = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabaseId == userDatabaseId)
            .ApplyPositionFilters(true, target.PosKey, PositionSearchMode.ExactPly, target.PlyCount)
            .CountAsync();

        var atAnotherPly = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabaseId == userDatabaseId)
            .ApplyPositionFilters(true, target.PosKey, PositionSearchMode.ExactPly, plyCount: 6)
            .CountAsync();

        // Both games reached the position, by different move orders.
        Assert.Equal(2, anyOrder);
        // Both reached it on ply 4, so pinning the ply keeps both here...
        Assert.Equal(2, atThisPly);
        // ...but a different ply excludes them, which is what the mode is for.
        Assert.Equal(0, atAnotherPly);
    }

    [Fact]
    public async Task ImportToDatabaseAsync_RejectsDatabaseOwnedBySomeoneElse()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (_, userDatabaseId) = await CreateOwnerAndDatabaseAsync(dbContext, "owner-user");
        var importService = CreateImportService(dbContext);

        using var reader = new StringReader("""
            [White "Alpha"]
            [Black "Beta"]
            [Result "*"]

            1. e4 e5 *
            """);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            importService.ImportToDatabaseAsync(reader, "intruder", userDatabaseId));
    }

    private static IDirectDatabaseImportService CreateImportService(ChessXivDbContext dbContext)
    {
        var serializer = new FenBoardStateSerializer();
        var factory = new BoardStateFactory(serializer);
        var transition = new BitboardBoardStateTransition();
        var positionCoordinator = new PositionImportCoordinator(factory, transition, new ZobristPositionKeyCalculator());

        return new DirectDatabaseImportService(
            new PgnService(),
            positionCoordinator,
            new GameRepository(dbContext),
            new UserDatabaseGameRepository(dbContext),
            new DraftPromotionRepository(dbContext),
            new EfUnitOfWork(dbContext));
    }

    private static async Task<(string OwnerId, Guid UserDatabaseId)> CreateOwnerAndDatabaseAsync(
        ChessXivDbContext dbContext,
        string ownerId)
    {
        dbContext.Users.Add(new ApplicationUser
        {
            Id = ownerId,
            UserName = ownerId,
            Email = $"{ownerId}@example.com"
        });

        var database = new UserDatabase
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            Name = $"db-{ownerId}",
            IsPublic = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.UserDatabases.Add(database);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return (ownerId, database.Id);
    }
}
