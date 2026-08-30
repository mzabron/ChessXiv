using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Factories;
using ChessXiv.Domain.Engine.Serialization;
using ChessXiv.Domain.Engine.Services;
using ChessXiv.Domain.Entities;
using ChessXiv.IntegrationTests.Infrastructure;
using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Repositories;

namespace ChessXiv.IntegrationTests;

/// <summary>
/// Exercises the opening tree against real PostgreSQL. The query is the performance-critical
/// path of the app, so it is worth proving it both translates and aggregates correctly.
/// </summary>
[Collection(PostgresCollection.Name)]
public class MoveTreeIntegrationTests(PostgresTestFixture fixture)
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterE4Fen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

    [Fact]
    public async Task MoveTree_AggregatesContinuationsAndResults_ForTheStartPosition()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, userDatabaseId) = await SeedAsync(dbContext, """
            [White "A"]
            [Black "B"]
            [Result "1-0"]

            1. e4 e5 2. Nf3 1-0
            """, """
            [White "C"]
            [Black "D"]
            [Result "0-1"]

            1. e4 c5 0-1
            """, """
            [White "E"]
            [Black "F"]
            [Result "1/2-1/2"]

            1. d4 d5 1/2-1/2
            """);

        var service = CreateService(dbContext);

        var result = await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = StartFen,
            Source = MoveTreeSource.UserDatabase,
            UserDatabaseId = userDatabaseId,
            MaxMoves = 10
        }, ownerId);

        Assert.Equal(3, result.TotalGamesInPosition);

        var e4 = result.Moves.Single(m => m.MoveSan == "e4");
        Assert.Equal(2, e4.Games);
        Assert.Equal(1, e4.WhiteWins);
        Assert.Equal(1, e4.BlackWins);
        Assert.Equal(0, e4.Draws);
        Assert.Equal(50m, e4.WhiteWinPct);

        var d4 = result.Moves.Single(m => m.MoveSan == "d4");
        Assert.Equal(1, d4.Games);
        Assert.Equal(1, d4.Draws);

        // Most frequent continuation first.
        Assert.Equal("e4", result.Moves[0].MoveSan);
    }

    [Fact]
    public async Task MoveTree_NarrowsToTheReachedPosition_DeeperInTheGame()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, userDatabaseId) = await SeedAsync(dbContext, """
            [White "A"]
            [Black "B"]
            [Result "1-0"]

            1. e4 e5 1-0
            """, """
            [White "C"]
            [Black "D"]
            [Result "0-1"]

            1. e4 c5 0-1
            """, """
            [White "E"]
            [Black "F"]
            [Result "1/2-1/2"]

            1. d4 d5 1/2-1/2
            """);

        var service = CreateService(dbContext);

        var result = await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = AfterE4Fen,
            Source = MoveTreeSource.UserDatabase,
            UserDatabaseId = userDatabaseId
        }, ownerId);

        // Only the two 1.e4 games reach this position; the 1.d4 game must not appear.
        Assert.Equal(2, result.TotalGamesInPosition);
        Assert.Equal(2, result.Moves.Count);
        Assert.Contains(result.Moves, m => m.MoveSan == "e5");
        Assert.Contains(result.Moves, m => m.MoveSan == "c5");
    }

    [Fact]
    public async Task MoveTree_AppliesGameFilters()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, userDatabaseId) = await SeedAsync(dbContext, """
            [White "A"]
            [Black "B"]
            [Result "1-0"]

            1. e4 e5 1-0
            """, """
            [White "C"]
            [Black "D"]
            [Result "0-1"]

            1. d4 d5 0-1
            """);

        var service = CreateService(dbContext);

        var result = await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = StartFen,
            Source = MoveTreeSource.UserDatabase,
            UserDatabaseId = userDatabaseId,
            Result = "1-0"
        }, ownerId);

        Assert.Equal(1, result.TotalGamesInPosition);
        Assert.Equal("e4", Assert.Single(result.Moves).MoveSan);
    }

    [Fact]
    public async Task MoveTree_ReturnsNothing_ForAPrivateDatabaseTheCallerDoesNotOwn()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (_, userDatabaseId) = await SeedAsync(dbContext, """
            [White "A"]
            [Black "B"]
            [Result "1-0"]

            1. e4 e5 1-0
            """);

        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<Application.Exceptions.ForbiddenException>(() =>
            service.GetMoveTreeAsync(new MoveTreeRequest
            {
                Fen = StartFen,
                Source = MoveTreeSource.UserDatabase,
                UserDatabaseId = userDatabaseId
            }, "intruder"));
    }

    private static GameExplorerService CreateService(ChessXivDbContext dbContext)
    {
        return new GameExplorerService(
            new GameExplorerRepository(dbContext),
            new FenBoardStateSerializer(),
            new ZobristPositionKeyCalculator());
    }

    private static async Task<(string OwnerId, Guid UserDatabaseId)> SeedAsync(
        ChessXivDbContext dbContext,
        params string[] pgns)
    {
        const string ownerId = "tree-owner";

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
            Name = "tree-db",
            IsPublic = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.UserDatabases.Add(database);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var serializer = new FenBoardStateSerializer();
        var coordinator = new PositionImportCoordinator(
            new BoardStateFactory(serializer),
            new BitboardBoardStateTransition(),
            new ZobristPositionKeyCalculator());

        var importService = new DirectDatabaseImportService(
            new PgnService(),
            coordinator,
            new GameRepository(dbContext),
            new UserDatabaseGameRepository(dbContext),
            new DraftPromotionRepository(dbContext),
            new EfUnitOfWork(dbContext));

        foreach (var pgn in pgns)
        {
            using var reader = new StringReader(pgn);
            await importService.ImportToDatabaseAsync(reader, ownerId, database.Id);
        }

        dbContext.ChangeTracker.Clear();
        return (ownerId, database.Id);
    }
}
