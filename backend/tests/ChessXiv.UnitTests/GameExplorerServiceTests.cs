using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Exceptions;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Models;

namespace ChessXiv.UnitTests;

public class GameExplorerServiceTests
{
    [Fact]
    public async Task SearchAsync_NormalizesPlayerTerms_AndPassesNormalizedNamesToRepository()
    {
        var explorerRepository = new FakeGameExplorerRepository
        {
            Response = new PagedResult<GameExplorerItemDto>
            {
                TotalCount = 1,
                Items = [new GameExplorerItemDto { GameId = Guid.NewGuid(), White = "Magnus Carlsen", Black = "Ian Nepomniachtchi", Result = "1-0" }]
            }
        };

        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionHasher());

        var request = new GameExplorerSearchRequest
        {
            UserDatabaseId = Guid.NewGuid(),
            WhiteFirstName = "  MAGNUS ",
            WhiteLastName = "CARLSEN"
        };

        var result = await service.SearchAsync(request, "user-1");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("magnus", explorerRepository.LastWhiteFirstName);
        Assert.Equal("carlsen", explorerRepository.LastWhiteLastName);
    }

    [Fact]
    public async Task SearchAsync_ComputesFenHash_ForExactSearch()
    {
        const string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        var explorerRepository = new FakeGameExplorerRepository();
        var serializer = new FakeBoardStateSerializer();
        var hasher = new FakePositionHasher { HashToReturn = 42UL };

        var service = new GameExplorerService(explorerRepository, serializer, hasher);

        await service.SearchAsync(new GameExplorerSearchRequest
        {
            UserDatabaseId = Guid.NewGuid(),
            SearchByPosition = true,
            PositionMode = PositionSearchMode.Exact,
            Fen = fen
        }, "user-1");

        Assert.Equal(fen, serializer.LastFenInput);
        Assert.Equal(unchecked((long)42UL), explorerRepository.LastFenHash);
        Assert.Equal(fen, explorerRepository.LastNormalizedFen);
    }

    [Fact]
    public async Task SearchAsync_WithNameFilters_DelegatesToRepository()
    {
        var explorerRepository = new FakeGameExplorerRepository();
        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionHasher());

        var result = await service.SearchAsync(new GameExplorerSearchRequest
        {
            UserDatabaseId = Guid.NewGuid(),
            WhiteLastName = "Carlsen"
        }, "user-1");

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
        Assert.Equal(1, explorerRepository.CallCount);
    }

    [Fact]
    public async Task SearchAsync_ThrowsForbidden_WhenUserDatabaseIsNotAccessible()
    {
        var explorerRepository = new FakeGameExplorerRepository
        {
            AccessStatus = UserDatabaseAccessStatus.Forbidden
        };

        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionHasher());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SearchAsync(new GameExplorerSearchRequest
            {
                UserDatabaseId = Guid.NewGuid()
            }, null));
    }

    [Fact]
    public async Task SearchAsync_ThrowsNotFound_WhenUserDatabaseDoesNotExist()
    {
        var explorerRepository = new FakeGameExplorerRepository
        {
            AccessStatus = UserDatabaseAccessStatus.NotFound
        };

        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionHasher());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.SearchAsync(new GameExplorerSearchRequest
            {
                UserDatabaseId = Guid.NewGuid()
            }, null));
    }

    [Fact]
    public async Task SearchAsync_ReturnsData_WhenUserDatabaseIsPublic()
    {
        var explorerRepository = new FakeGameExplorerRepository
        {
            AccessStatus = UserDatabaseAccessStatus.Accessible,
            Response = new PagedResult<GameExplorerItemDto>
            {
                TotalCount = 1,
                Items = [new GameExplorerItemDto { GameId = Guid.NewGuid(), White = "Public", Black = "Database", Result = "1-0" }]
            }
        };

        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionHasher());

        var result = await service.SearchAsync(new GameExplorerSearchRequest
        {
            UserDatabaseId = Guid.NewGuid()
        }, "user-a");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, explorerRepository.CallCount);
    }

    [Fact]
    public async Task SearchAsync_GuestWithoutUserDatabaseId_DelegatesToRepository()
    {
        var explorerRepository = new FakeGameExplorerRepository
        {
            Response = new PagedResult<GameExplorerItemDto>
            {
                TotalCount = 1,
                Items = [new GameExplorerItemDto { GameId = Guid.NewGuid(), White = "Alpha", Black = "Beta", Result = "*" }]
            }
        };

        var service = new GameExplorerService(
            explorerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionHasher());

        var result = await service.SearchAsync(new GameExplorerSearchRequest(), null);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, explorerRepository.CallCount);
    }

    [Fact]
    public async Task GetMoveTreeAsync_ComputesFenHash_AndCalculatesPercentages()
    {
        const string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        var explorerRepository = new FakeGameExplorerRepository
        {
            MoveTreeResponse = new MoveTreeResponse
            {
                TotalGamesInPosition = 10,
                Moves =
                [
                    new MoveTreeMoveDto
                    {
                        MoveSan = "e4",
                        Games = 5,
                        WhiteWins = 3,
                        Draws = 1,
                        BlackWins = 1
                    },
                    new MoveTreeMoveDto
                    {
                        MoveSan = "d4",
                        Games = 2,
                        WhiteWins = 0,
                        Draws = 1,
                        BlackWins = 1
                    }
                ]
            }
        };

        var serializer = new FakeBoardStateSerializer();
        var hasher = new FakePositionHasher { HashToReturn = 99UL };
        var service = new GameExplorerService(explorerRepository, serializer, hasher);

        var result = await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = fen,
            Source = MoveTreeSource.UserDatabase,
            UserDatabaseId = Guid.NewGuid(),
            MaxMoves = 10
        }, "user-1");

        Assert.Equal(fen, serializer.LastFenInput);
        Assert.Equal(fen, explorerRepository.LastMoveTreeNormalizedFen);
        Assert.Equal(unchecked((long)99UL), explorerRepository.LastMoveTreeFenHash);
        Assert.Equal(60m, result.Moves[0].WhiteWinPct);
        Assert.Equal(20m, result.Moves[0].DrawPct);
        Assert.Equal(20m, result.Moves[0].BlackWinPct);
        Assert.Equal(0m, result.Moves[1].WhiteWinPct);
        Assert.Equal(50m, result.Moves[1].DrawPct);
        Assert.Equal(50m, result.Moves[1].BlackWinPct);
    }

    [Fact]
    public async Task GetMoveTreeAsync_DoesNotThrow_WhenMoveHasZeroGames()
    {
        const string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        var explorerRepository = new FakeGameExplorerRepository
        {
            MoveTreeResponse = new MoveTreeResponse
            {
                TotalGamesInPosition = 0,
                Moves =
                [
                    new MoveTreeMoveDto
                    {
                        MoveSan = "e4",
                        Games = 0,
                        WhiteWins = 0,
                        Draws = 0,
                        BlackWins = 0
                    }
                ]
            }
        };

        var serializer = new FakeBoardStateSerializer();
        var hasher = new FakePositionHasher();
        var service = new GameExplorerService(explorerRepository, serializer, hasher);

        var result = await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = fen,
            Source = MoveTreeSource.UserDatabase,
            UserDatabaseId = Guid.NewGuid()
        }, "user-1");

        Assert.Single(result.Moves);
        Assert.Equal(0, result.Moves[0].Games);
        Assert.Equal(0m, result.Moves[0].WhiteWinPct);
        Assert.Equal(0m, result.Moves[0].DrawPct);
        Assert.Equal(0m, result.Moves[0].BlackWinPct);
    }

    [Fact]
    public async Task GetMoveTreeAsync_NormalizesMoveTreeFilters_AndForwardsFilterFenHash()
    {
        const string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        var explorerRepository = new FakeGameExplorerRepository();
        var serializer = new FakeBoardStateSerializer();
        var hasher = new FakePositionHasher { HashToReturn = 77UL };
        var service = new GameExplorerService(explorerRepository, serializer, hasher);

        await service.GetMoveTreeAsync(new MoveTreeRequest
        {
            Fen = fen,
            WhiteFirstName = "  MAGNUS ",
            WhiteLastName = " CARLSEN ",
            BlackFirstName = " ian ",
            SearchByPosition = true,
            FilterFen = fen,
            PositionMode = PositionSearchMode.SamePosition,
            Source = MoveTreeSource.StagingSession
        }, "user-1");

        Assert.Equal("magnus", explorerRepository.LastMoveTreeWhiteFirstName);
        Assert.Equal("carlsen", explorerRepository.LastMoveTreeWhiteLastName);
        Assert.Equal("ian", explorerRepository.LastMoveTreeBlackFirstName);
        Assert.Equal(fen, explorerRepository.LastMoveTreeNormalizedFilterFen);
        Assert.Equal(unchecked((long)77UL), explorerRepository.LastMoveTreeFilterFenHash);
    }

    private sealed class FakeGameExplorerRepository : IGameExplorerRepository
    {
        public int CallCount { get; private set; }
        public string? LastWhiteFirstName { get; private set; }
        public string? LastWhiteLastName { get; private set; }
        public string? LastBlackFirstName { get; private set; }
        public string? LastBlackLastName { get; private set; }
        public string? LastNormalizedFen { get; private set; }
        public long? LastFenHash { get; private set; }
        public PagedResult<GameExplorerItemDto> Response { get; set; } = new();
        public MoveTreeResponse MoveTreeResponse { get; set; } = new();
        public UserDatabaseAccessStatus AccessStatus { get; set; } = UserDatabaseAccessStatus.Accessible;
        public MoveTreeRequest? LastMoveTreeRequest { get; private set; }
        public string? LastMoveTreeOwnerUserId { get; private set; }
        public string? LastMoveTreeWhiteFirstName { get; private set; }
        public string? LastMoveTreeWhiteLastName { get; private set; }
        public string? LastMoveTreeBlackFirstName { get; private set; }
        public string? LastMoveTreeBlackLastName { get; private set; }
        public string? LastMoveTreeNormalizedFen { get; private set; }
        public long? LastMoveTreeFenHash { get; private set; }
        public string? LastMoveTreeNormalizedFilterFen { get; private set; }
        public long? LastMoveTreeFilterFenHash { get; private set; }

        public Task<UserDatabaseAccessStatus> GetUserDatabaseAccessStatusAsync(
            Guid userDatabaseId,
            string? ownerUserId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AccessStatus);
        }

        public Task<PagedResult<GameExplorerItemDto>> SearchAsync(
            GameExplorerSearchRequest request,
            string? ownerUserId,
            string? normalizedWhiteFirstName,
            string? normalizedWhiteLastName,
            string? normalizedBlackFirstName,
            string? normalizedBlackLastName,
            string? normalizedFen,
            long? fenHash,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastWhiteFirstName = normalizedWhiteFirstName;
            LastWhiteLastName = normalizedWhiteLastName;
            LastBlackFirstName = normalizedBlackFirstName;
            LastBlackLastName = normalizedBlackLastName;
            LastNormalizedFen = normalizedFen;
            LastFenHash = fenHash;
            return Task.FromResult(Response);
        }

        public Task<MoveTreeResponse> GetMoveTreeAsync(
            MoveTreeRequest request,
            string ownerUserId,
            string? normalizedWhiteFirstName,
            string? normalizedWhiteLastName,
            string? normalizedBlackFirstName,
            string? normalizedBlackLastName,
            string normalizedFen,
            long fenHash,
            string? normalizedFilterFen,
            long? filterFenHash,
            CancellationToken cancellationToken = default)
        {
            LastMoveTreeRequest = request;
            LastMoveTreeOwnerUserId = ownerUserId;
            LastMoveTreeWhiteFirstName = normalizedWhiteFirstName;
            LastMoveTreeWhiteLastName = normalizedWhiteLastName;
            LastMoveTreeBlackFirstName = normalizedBlackFirstName;
            LastMoveTreeBlackLastName = normalizedBlackLastName;
            LastMoveTreeNormalizedFen = normalizedFen;
            LastMoveTreeFenHash = fenHash;
            LastMoveTreeNormalizedFilterFen = normalizedFilterFen;
            LastMoveTreeFilterFenHash = filterFenHash;
            return Task.FromResult(MoveTreeResponse);
        }
    }

    private sealed class FakeBoardStateSerializer : IBoardStateSerializer
    {
        public string? LastFenInput { get; private set; }

        public BoardState FromFen(string fen)
        {
            LastFenInput = fen;
            return new BoardState();
        }

        public string ToFen(in BoardState state)
        {
            return string.Empty;
        }
    }

    private sealed class FakePositionHasher : IPositionHasher
    {
        public ulong HashToReturn { get; set; } = 1UL;

        public ulong Compute(in BoardState state)
        {
            return HashToReturn;
        }
    }
}
