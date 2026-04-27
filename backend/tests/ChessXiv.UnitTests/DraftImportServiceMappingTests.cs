using ChessXiv.Application.Services;
using ChessXiv.Domain.Entities;

namespace ChessXiv.UnitTests;

public class DraftImportServiceMappingTests
{
    [Fact]
    public void MapToStagingGame_MapsYearMoveCountAndMoveMetadata()
    {
        var createdAtUtc = DateTime.UtcNow;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            White = "Alpha",
            Black = "Beta",
            Date = new DateTime(2024, 7, 11, 0, 0, 0, DateTimeKind.Utc),
            Result = "*",
            Pgn = "dummy",
            Moves =
            [
                new Move
                {
                    Id = Guid.NewGuid(),
                    MoveNumber = 1,
                    WhiteMove = "e4",
                    BlackMove = "e5",
                    WhiteClk = "01:20:00",
                    BlackClk = "01:19:59"
                }
            ]
        };

        var staging = DraftImportService.MapToStagingGame(game, "user-1", createdAtUtc);

        Assert.Equal("user-1", staging.OwnerUserId);
        Assert.Equal(createdAtUtc, staging.CreatedAtUtc);
        Assert.Equal(2024, staging.Year);
        Assert.Equal(1, staging.MoveCount);
        Assert.Single(staging.Moves);
        Assert.Equal("01:20:00", staging.Moves.First().WhiteClk);
        Assert.Equal("01:19:59", staging.Moves.First().BlackClk);
    }

    [Fact]
    public void MapToTransientGame_PreservesClockMetadata()
    {
        var staging = new StagingGame
        {
            Id = Guid.NewGuid(),
            White = "Alpha",
            Black = "Beta",
            Result = "*",
            Pgn = "dummy",
            Moves =
            [
                new StagingMove
                {
                    Id = Guid.NewGuid(),
                    MoveNumber = 1,
                    WhiteMove = "Nf3",
                    BlackMove = "d5",
                    WhiteClk = "00:59:58",
                    BlackClk = "00:59:50"
                }
            ]
        };

        var transient = DraftImportService.MapToTransientGame(staging);

        Assert.Single(transient.Moves);
        var move = transient.Moves.First();
        Assert.Equal("Nf3", move.WhiteMove);
        Assert.Equal("d5", move.BlackMove);
        Assert.Equal("00:59:58", move.WhiteClk);
        Assert.Equal("00:59:50", move.BlackClk);
    }
}
