using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Factories;
using ChessXiv.Domain.Engine.Serialization;
using ChessXiv.Domain.Engine.Services;
using ChessXiv.IntegrationTests.Infrastructure;
using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class PgnImportPersistenceTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task ImportAsync_PersistsGamesAndMoves_InSameDatabase()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var parser = new PgnService();
        var repository = new GameRepository(dbContext);
        var serializer = new FenBoardStateSerializer();
        var factory = new BoardStateFactory(serializer);
        var transition = new BitboardBoardStateTransition();
        var hasher = new ZobristPositionHasher();
        var positionCoordinator = new PositionImportCoordinator(factory, serializer, transition, hasher);
        var unitOfWork = new EfUnitOfWork(dbContext);
        var importService = new PgnImportService(parser, repository, positionCoordinator, unitOfWork);

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
        var result = await importService.ImportAsync(reader);

        Assert.Equal(1, result.ParsedCount);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);

        var gamesCount = await dbContext.Games.CountAsync();
        var movesCount = await dbContext.Moves.CountAsync();
        var positionsCount = await dbContext.Positions.CountAsync();
        Assert.Equal(1, gamesCount);
        Assert.Equal(2, movesCount);
        Assert.Equal(5, positionsCount);

        var savedGame = await dbContext.Games
            .Include(game => game.Moves)
            .Include(game => game.Positions)
            .SingleAsync();

        Assert.Equal("Alpha", savedGame.White);
        Assert.Equal("Beta", savedGame.Black);
        Assert.Equal("1-0", savedGame.Result);
        Assert.Equal(2, savedGame.Moves.Count);

        var moveOne = savedGame.Moves.Single(move => move.MoveNumber == 1);
        Assert.Equal("e4", moveOne.WhiteMove);
        Assert.Equal("c5", moveOne.BlackMove);
        Assert.Equal(0.18, moveOne.WhiteEval);
        Assert.Equal(0.25, moveOne.BlackEval);
        Assert.Equal("0:10:00", moveOne.WhiteClk);
        Assert.Equal("0:09:58", moveOne.BlackClk);

        var maxPly = savedGame.Positions.Max(position => position.PlyCount);
        Assert.Equal(4, maxPly);
    }

    [Fact]
    public async Task ImportAsync_PersistsGame_WhenLastBlackMoveIsMissing()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var parser = new PgnService();
        var repository = new GameRepository(dbContext);
        var serializer = new FenBoardStateSerializer();
        var factory = new BoardStateFactory(serializer);
        var transition = new BitboardBoardStateTransition();
        var hasher = new ZobristPositionHasher();
        var positionCoordinator = new PositionImportCoordinator(factory, serializer, transition, hasher);
        var unitOfWork = new EfUnitOfWork(dbContext);
        var importService = new PgnImportService(parser, repository, positionCoordinator, unitOfWork);

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
        var result = await importService.ImportAsync(reader);

        Assert.Equal(1, result.ParsedCount);
        Assert.Equal(1, result.ImportedCount);

        var savedGame = await dbContext.Games
            .Include(game => game.Moves)
            .Include(game => game.Positions)
            .SingleAsync();

        var moveTwo = savedGame.Moves.Single(move => move.MoveNumber == 2);
        Assert.Equal("Nf3", moveTwo.WhiteMove);
        Assert.Null(moveTwo.BlackMove);

        var maxPly = savedGame.Positions.Max(position => position.PlyCount);
        Assert.Equal(3, maxPly);
    }
}
