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
    public async Task SearchAsync_NormalizesPlayerTerms_AndPassesResolvedIdsToRepository()
    {
        var expectedPlayerId = Guid.NewGuid();
        var playerRepository = new FakePlayerRepository
        {
            SearchIdsResult = [expectedPlayerId]
        };

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
            playerRepository,
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
        Assert.Equal("magnus", playerRepository.LastFirstName);
        Assert.Equal("carlsen", playerRepository.LastLastName);
        Assert.Single(explorerRepository.LastWhitePlayerIds!);
        Assert.Contains(expectedPlayerId, explorerRepository.LastWhitePlayerIds!);
    }

    [Fact]
    public async Task SearchAsync_ComputesFenHash_ForExactSearch()
    {
        const string fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        var playerRepository = new FakePlayerRepository();
        var explorerRepository = new FakeGameExplorerRepository();
        var serializer = new FakeBoardStateSerializer();
        var hasher = new FakePositionHasher { HashToReturn = 42UL };

        var service = new GameExplorerService(explorerRepository, playerRepository, serializer, hasher);

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
    public async Task SearchAsync_ReturnsEmpty_WhenNoPlayerIdsFound_AndSkipsGameRepository()
    {
        var playerRepository = new FakePlayerRepository
        {
            SearchIdsResult = []
        };

        var explorerRepository = new FakeGameExplorerRepository();
        var service = new GameExplorerService(
            explorerRepository,
            playerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionHasher());

        var result = await service.SearchAsync(new GameExplorerSearchRequest
        {
            UserDatabaseId = Guid.NewGuid(),
            WhiteLastName = "Carlsen"
        }, "user-1");

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
        Assert.Equal(0, explorerRepository.CallCount);
    }

    [Fact]
    public async Task SearchAsync_ThrowsForbidden_WhenUserDatabaseIsNotAccessible()
    {
        var playerRepository = new FakePlayerRepository();
        var explorerRepository = new FakeGameExplorerRepository
        {
            AccessStatus = UserDatabaseAccessStatus.Forbidden
        };

        var service = new GameExplorerService(
            explorerRepository,
            playerRepository,
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
        var playerRepository = new FakePlayerRepository();
        var explorerRepository = new FakeGameExplorerRepository
        {
            AccessStatus = UserDatabaseAccessStatus.NotFound
        };

        var service = new GameExplorerService(
            explorerRepository,
            playerRepository,
            new FakeBoardStateSerializer(),
            new FakePositionHasher());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.SearchAsync(new GameExplorerSearchRequest
            {
                UserDatabaseId = Guid.NewGuid()
            }, null));
    }

    [Fact]
    public async Task SearchAsync_GuestWithoutUserDatabaseId_DelegatesToRepository()
    {
        var playerRepository = new FakePlayerRepository();
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
            playerRepository,
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

        var playerRepository = new FakePlayerRepository();
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
        var service = new GameExplorerService(explorerRepository, playerRepository, serializer, hasher);

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

        var playerRepository = new FakePlayerRepository();
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
        var service = new GameExplorerService(explorerRepository, playerRepository, serializer, hasher);

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

    private sealed class FakeGameExplorerRepository : IGameExplorerRepository
    {
        public int CallCount { get; private set; }
        public IReadOnlyCollection<Guid>? LastWhitePlayerIds { get; private set; }
        public IReadOnlyCollection<Guid>? LastBlackPlayerIds { get; private set; }
        public string? LastNormalizedFen { get; private set; }
        public long? LastFenHash { get; private set; }
        public PagedResult<GameExplorerItemDto> Response { get; set; } = new();
        public MoveTreeResponse MoveTreeResponse { get; set; } = new();
        public UserDatabaseAccessStatus AccessStatus { get; set; } = UserDatabaseAccessStatus.Accessible;
        public MoveTreeRequest? LastMoveTreeRequest { get; private set; }
        public string? LastMoveTreeOwnerUserId { get; private set; }
        public string? LastMoveTreeNormalizedFen { get; private set; }
        public long? LastMoveTreeFenHash { get; private set; }

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
            IReadOnlyCollection<Guid>? whitePlayerIds,
            IReadOnlyCollection<Guid>? blackPlayerIds,
            string? normalizedFen,
            long? fenHash,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastWhitePlayerIds = whitePlayerIds;
            LastBlackPlayerIds = blackPlayerIds;
            LastNormalizedFen = normalizedFen;
            LastFenHash = fenHash;
            return Task.FromResult(Response);
        }

        public Task<MoveTreeResponse> GetMoveTreeAsync(
            MoveTreeRequest request,
            string ownerUserId,
            string normalizedFen,
            long fenHash,
            CancellationToken cancellationToken = default)
        {
            LastMoveTreeRequest = request;
            LastMoveTreeOwnerUserId = ownerUserId;
            LastMoveTreeNormalizedFen = normalizedFen;
            LastMoveTreeFenHash = fenHash;
            return Task.FromResult(MoveTreeResponse);
        }
    }

    private sealed class FakePlayerRepository : IPlayerRepository
    {
        public IReadOnlyCollection<Guid> SearchIdsResult { get; set; } = [];
        public string? LastFirstName { get; private set; }
        public string? LastLastName { get; private set; }

        public Task<IReadOnlyDictionary<string, ChessXiv.Domain.Entities.Player>> GetByNormalizedFullNamesAsync(
            IReadOnlyCollection<string> normalizedFullNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, ChessXiv.Domain.Entities.Player>>(
                new Dictionary<string, ChessXiv.Domain.Entities.Player>(StringComparer.Ordinal));
        }

        public Task AddRangeAsync(IReadOnlyCollection<ChessXiv.Domain.Entities.Player> players, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<Guid>> SearchIdsAsync(
            string? normalizedFirstName,
            string? normalizedLastName,
            CancellationToken cancellationToken = default)
        {
            LastFirstName = normalizedFirstName;
            LastLastName = normalizedLastName;
            return Task.FromResult(SearchIdsResult);
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
