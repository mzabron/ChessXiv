using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Entities;

namespace ChessXiv.UnitTests;

public class DraftPromotionServiceTests
{
    [Fact]
    public async Task PromoteAsync_ResolvesPlayers_ForPromotedGames()
    {
        var userDatabaseId = Guid.NewGuid();
        var stagingGame = CreateStagingGame("user-1", "hash-2", "Event", "Site");
        stagingGame.White = "Carlsen, Magnus";
        stagingGame.Black = "Nakamura, Hikaru";

        var repo = new FakeDraftPromotionRepository(stagingGame, userDatabaseId, "user-1");
        var unitOfWork = new FakeUnitOfWork();
        var service = new DraftPromotionService(repo, unitOfWork);

        var result = await service.PromoteAsync("user-1", userDatabaseId);

        Assert.Equal(1, result.PromotedCount);
        Assert.Equal(1, repo.PromoteAllCallCount);
    }

    [Fact]
    public async Task PromoteAsync_ProcessesThreeNonEmptyBatches_For1001Games()
    {
        var userDatabaseId = Guid.NewGuid();
        var games = Enumerable.Range(1, 1001)
            .Select(i => CreateStagingGame("user-1", $"hash-{i}", "Event", "Site"))
            .ToArray();

        var repo = new FakeDraftPromotionRepository(games, userDatabaseId, "user-1");
        var unitOfWork = new FakeUnitOfWork();
        var service = new DraftPromotionService(repo, unitOfWork);

        var result = await service.PromoteAsync("user-1", userDatabaseId);

        Assert.Equal(1001, result.PromotedCount);
        Assert.Equal(1, repo.PromoteAllCallCount);
    }

    private static StagingGame CreateStagingGame(string ownerUserId, string hash, string @event, string site)
    {
        return new StagingGame
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            CreatedAtUtc = DateTime.UtcNow,
            White = "Alpha",
            Black = "Beta",
            Result = "*",
            Pgn = "1. e4 e5 *",
            MoveCount = 1,
            GameHash = hash,
            Event = @event,
            Site = site,
            Round = "3",
            Moves =
            [
                new StagingMove
                {
                    Id = Guid.NewGuid(),
                    MoveNumber = 1,
                    WhiteMove = "e4",
                    BlackMove = "e5"
                }
            ],
            Positions =
            [
                new StagingPosition
                {
                    Id = Guid.NewGuid(),
                    PlyCount = 0,
                    Fen = "startpos",
                    FenHash = 123,
                    SideToMove = 'w'
                }
            ]
        };
    }

    private sealed class FakeDraftPromotionRepository : IDraftPromotionRepository
    {
        private readonly Guid _userDatabaseId;
        private readonly string _ownerUserId;
        private readonly List<StagingGame> _stagingGames;
        public FakeDraftPromotionRepository(StagingGame stagingGame, Guid userDatabaseId, string ownerUserId)
            : this([stagingGame], userDatabaseId, ownerUserId)
        {
        }

        public FakeDraftPromotionRepository(
            IReadOnlyCollection<StagingGame> stagingGames,
            Guid userDatabaseId,
            string ownerUserId)
        {
            _userDatabaseId = userDatabaseId;
            _ownerUserId = ownerUserId;
            _stagingGames = stagingGames.ToList();
        }

        public int PromoteAllCallCount { get; private set; }

        public Task<UserDatabase?> GetUserDatabaseAsync(Guid userDatabaseId, CancellationToken cancellationToken = default)
        {
            if (userDatabaseId != _userDatabaseId)
            {
                return Task.FromResult<UserDatabase?>(null);
            }

            return Task.FromResult<UserDatabase?>(new UserDatabase
            {
                Id = _userDatabaseId,
                OwnerUserId = _ownerUserId,
                Name = "db",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        public Task<int> PromoteAllAsync(
            string ownerUserId,
            Guid userDatabaseId,
            DateTime addedAtUtc,
            CancellationToken cancellationToken = default)
        {
            PromoteAllCallCount++;

            if (!string.Equals(ownerUserId, _ownerUserId, StringComparison.Ordinal) || userDatabaseId != _userDatabaseId)
            {
                return Task.FromResult(0);
            }

            var promoted = _stagingGames.Count;
            _stagingGames.Clear();
            return Task.FromResult(promoted);
        }

    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction());
        }

        public void ClearTracker()
        {
        }
    }

    private sealed class FakeTransaction : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
