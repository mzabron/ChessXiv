using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;
using ChessXiv.Application.Exceptions;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Models;

namespace ChessXiv.UnitTests;

public class GameExplorerServiceTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public async Task GetMoveTreeAsync_ResolvesPositionKey_AndCalculatesPercentages()
    {
        var explorerRepository = new FakeGameExplorerRepository
        {
            MoveTreeResponse = new MoveTreeResponse
            {
                TotalGamesInPosition = 10,
                Moves =
                [
                    new MoveTreeMoveDto { MoveSan = "e4", Games = 5, WhiteWins = 3, Draws = 1, BlackWins = 1 },
                    new MoveTreeMoveDto { MoveSan = "d4", Games = 2, WhiteWins = 0, Draws = 1, BlackWins = 1 }
                ]
            }
        };

        var serializer = new FakeBoardStateSerializer();
        var keyCalculator = new FakePositionKeyCalculator { KeyToReturn = [1, 2, 3] };
        var service = new GameExplorerService(explorerRepository, serializer, keyCalculator);

        var result = await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = StartFen,
            Source = MoveTreeSource.UserDatabase,
            UserDatabaseId = Guid.NewGuid(),
            MaxMoves = 10
        }, "user-1");

        Assert.Equal(StartFen, serializer.LastFenInput);
        Assert.Equal<byte[]>([1, 2, 3], explorerRepository.LastPosKey!);
        Assert.Null(explorerRepository.LastFilterTarget);

        Assert.Equal(60m, result.Moves[0].WhiteWinPct);
        Assert.Equal(20m, result.Moves[0].DrawPct);
        Assert.Equal(20m, result.Moves[0].BlackWinPct);
        Assert.Equal(0m, result.Moves[1].WhiteWinPct);
        Assert.Equal(50m, result.Moves[1].DrawPct);
        Assert.Equal(50m, result.Moves[1].BlackWinPct);
    }

    [Fact]
    public async Task GetMoveTreeAsync_IgnoresResultlessGames_WhenCalculatingPercentages()
    {
        // Four of the six games carry a result; the other two are "*" (unfinished or
        // missing the tag). The three percentages must still describe the decided games
        // and add up to 100 - dividing by Games would give 25/25/0 and leave half the
        // win/draw bar unaccounted for.
        var explorerRepository = new FakeGameExplorerRepository
        {
            MoveTreeResponse = new MoveTreeResponse
            {
                TotalGamesInPosition = 6,
                Moves = [new MoveTreeMoveDto { MoveSan = "e4", Games = 6, WhiteWins = 2, Draws = 1, BlackWins = 1 }]
            }
        };

        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionKeyCalculator { KeyToReturn = [1, 2, 3] });

        var result = await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = StartFen,
            Source = MoveTreeSource.UserDatabase,
            UserDatabaseId = Guid.NewGuid()
        }, "user-1");

        var move = Assert.Single(result.Moves);
        Assert.Equal(50m, move.WhiteWinPct);
        Assert.Equal(25m, move.DrawPct);
        Assert.Equal(25m, move.BlackWinPct);
        Assert.Equal(100m, move.WhiteWinPct + move.DrawPct + move.BlackWinPct);
    }

    [Fact]
    public async Task GetMoveTreeAsync_DoesNotDivideByZero_WhenMoveHasNoGames()
    {
        var explorerRepository = new FakeGameExplorerRepository
        {
            MoveTreeResponse = new MoveTreeResponse
            {
                TotalGamesInPosition = 0,
                Moves = [new MoveTreeMoveDto { MoveSan = "e4", Games = 0 }]
            }
        };

        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionKeyCalculator());

        var result = await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = StartFen,
            Source = MoveTreeSource.UserDatabase,
            UserDatabaseId = Guid.NewGuid()
        }, "user-1");

        Assert.Single(result.Moves);
        Assert.Equal(0m, result.Moves[0].WhiteWinPct);
        Assert.Equal(0m, result.Moves[0].DrawPct);
        Assert.Equal(0m, result.Moves[0].BlackWinPct);
    }

    [Fact]
    public async Task GetMoveTreeAsync_NormalizesPlayerFilters_AndForwardsFilterPositionKey()
    {
        var explorerRepository = new FakeGameExplorerRepository();
        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionKeyCalculator { KeyToReturn = [7, 7] });

        await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = StartFen,
            WhiteFirstName = "  MAGNUS ",
            WhiteLastName = " CARLSEN ",
            BlackFirstName = " ian ",
            SearchByPosition = true,
            FilterFen = StartFen,
            Source = MoveTreeSource.StagingSession
        }, "user-1");

        Assert.Equal("magnus", explorerRepository.LastWhiteFirstName);
        Assert.Equal("carlsen", explorerRepository.LastWhiteLastName);
        Assert.Equal("ian", explorerRepository.LastBlackFirstName);
        Assert.Equal<byte[]>([7, 7], explorerRepository.LastFilterTarget!.PosKey);
    }

    [Fact]
    public async Task GetMoveTreeAsync_ReturnsNothing_WhenPositionFilterFenIsUnparseable()
    {
        var explorerRepository = new FakeGameExplorerRepository();
        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer { ThrowOnFen = "bogus" },
            new FakePositionKeyCalculator());

        var result = await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = StartFen,
            SearchByPosition = true,
            FilterFen = "bogus",
            Source = MoveTreeSource.StagingSession
        }, "user-1");

        Assert.Empty(result.Moves);
        Assert.Equal(0, explorerRepository.CallCount);
    }

    [Fact]
    public async Task GetMoveTreeAsync_ThrowsForbidden_WhenUserDatabaseIsNotAccessible()
    {
        var explorerRepository = new FakeGameExplorerRepository
        {
            AccessStatus = UserDatabaseAccessStatus.Forbidden
        };

        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionKeyCalculator());

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = StartFen,
            Source = MoveTreeSource.UserDatabase,
            UserDatabaseId = Guid.NewGuid()
        }, "someone-else"));
    }

    [Fact]
    public async Task GetMoveTreeAsync_ThrowsNotFound_WhenUserDatabaseDoesNotExist()
    {
        var explorerRepository = new FakeGameExplorerRepository
        {
            AccessStatus = UserDatabaseAccessStatus.NotFound
        };

        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionKeyCalculator());

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = StartFen,
            Source = MoveTreeSource.UserDatabase,
            UserDatabaseId = Guid.NewGuid()
        }, "user-1"));
    }

    private sealed class FakeGameExplorerRepository : IGameExplorerRepository
    {
        public int CallCount { get; private set; }
        public MoveTreeResponse MoveTreeResponse { get; set; } = new();
        public UserDatabaseAccessStatus AccessStatus { get; set; } = UserDatabaseAccessStatus.Accessible;
        public string? LastWhiteFirstName { get; private set; }
        public string? LastWhiteLastName { get; private set; }
        public string? LastBlackFirstName { get; private set; }
        public string? LastBlackLastName { get; private set; }
        public byte[]? LastPosKey { get; private set; }
        public PositionSearchTarget? LastFilterTarget { get; private set; }

        public Task<UserDatabaseAccessStatus> GetUserDatabaseAccessStatusAsync(
            Guid userDatabaseId,
            string? ownerUserId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AccessStatus);
        }

        public Task<MoveTreeResponse> GetMoveTreeAsync(
            MoveTreeRequest request,
            string? ownerUserId,
            string? normalizedWhiteFirstName,
            string? normalizedWhiteLastName,
            string? normalizedBlackFirstName,
            string? normalizedBlackLastName,
            byte[] posKey,
            PositionSearchTarget? filterTarget,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastWhiteFirstName = normalizedWhiteFirstName;
            LastWhiteLastName = normalizedWhiteLastName;
            LastBlackFirstName = normalizedBlackFirstName;
            LastBlackLastName = normalizedBlackLastName;
            LastPosKey = posKey;
            LastFilterTarget = filterTarget;
            return Task.FromResult(MoveTreeResponse);
        }
    }

    private sealed class FakeBoardStateSerializer : IBoardStateSerializer
    {
        public string? LastFenInput { get; private set; }
        public string? ThrowOnFen { get; init; }

        public BoardState FromFen(string fen)
        {
            LastFenInput = fen;

            if (ThrowOnFen is not null && string.Equals(fen, ThrowOnFen, StringComparison.Ordinal))
            {
                throw new FormatException("Invalid FEN.");
            }

            return new BoardState();
        }

        public string ToFen(in BoardState state) => string.Empty;
    }

    private sealed class FakePositionKeyCalculator : IPositionKeyCalculator
    {
        public byte[] KeyToReturn { get; set; } = [1];

        public byte[] Compute(in BoardState state) => KeyToReturn;
    }
}
