using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Models;
using ChessXiv.Domain.Entities;

namespace ChessXiv.UnitTests;

public class DraftImportServiceMappingTests
{
    [Fact]
    public void MapToStagingGame_DerivesYearAndMoveCount_FromTheParsedGame()
    {
        var createdAtUtc = DateTime.UtcNow;
        var parsed = new ParsedGame
        {
            Game = new Game
            {
                Id = Guid.NewGuid(),
                White = "Alpha",
                Black = "Beta",
                Date = new DateTime(2024, 7, 11, 0, 0, 0, DateTimeKind.Utc),
                Result = "1-0",
                Pgn = "dummy"
            },
            Moves =
            [
                new ParsedMove { MoveNumber = 1, WhiteMove = "e4", BlackMove = "e5" },
                new ParsedMove { MoveNumber = 2, WhiteMove = "Nf3" }
            ]
        };

        var staging = DraftImportService.MapToStagingGame(parsed, "user-1", createdAtUtc);

        Assert.Equal("user-1", staging.OwnerUserId);
        Assert.Equal(createdAtUtc, staging.CreatedAtUtc);
        Assert.Equal(2024, staging.Year);
        Assert.Equal(2, staging.MoveCount);
        Assert.False(string.IsNullOrWhiteSpace(staging.GameHash));
    }

    [Fact]
    public void MapToStagingGame_NormalizesPlayerNames_ForFiltering()
    {
        var parsed = new ParsedGame
        {
            Game = new Game
            {
                Id = Guid.NewGuid(),
                White = "Carlsen, Magnus",
                Black = "Nakamura, Hikaru",
                Result = "*",
                Pgn = "dummy"
            },
            Moves = []
        };

        var staging = DraftImportService.MapToStagingGame(parsed, "user-1", DateTime.UtcNow);

        Assert.Equal("magnus", staging.WhiteNormalizedFirstName);
        Assert.Equal("carlsen", staging.WhiteNormalizedLastName);
        Assert.Equal("hikaru", staging.BlackNormalizedFirstName);
        Assert.Equal("nakamura", staging.BlackNormalizedLastName);
    }

    [Fact]
    public void MapToStagingGame_CarriesGeneratedPositionsOntoTheStagingRow()
    {
        var gameId = Guid.NewGuid();
        var parsed = new ParsedGame
        {
            Game = new Game
            {
                Id = gameId,
                White = "Alpha",
                Black = "Beta",
                Result = "0-1",
                Pgn = "dummy",
                Positions =
                [
                    new Position { GameId = gameId, PlyCount = 0, PosKey = [1, 2], NextMove = "e4", Result = GameResult.BlackWin },
                    new Position { GameId = gameId, PlyCount = 1, PosKey = [3, 4], Result = GameResult.BlackWin }
                ]
            },
            Moves = [new ParsedMove { MoveNumber = 1, WhiteMove = "e4" }]
        };

        var staging = DraftImportService.MapToStagingGame(parsed, "user-1", DateTime.UtcNow);

        var positions = staging.Positions.OrderBy(p => p.PlyCount).ToList();
        Assert.Equal(2, positions.Count);
        Assert.All(positions, p => Assert.Equal(staging.Id, p.StagingGameId));
        Assert.Equal("e4", positions[0].NextMove);
        Assert.Null(positions[1].NextMove);
        Assert.All(positions, p => Assert.Equal(GameResult.BlackWin, p.Result));
    }
}
