using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
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
public class DraftPromotionIntegrationTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task DraftImport_WhenQuotaExceeded_ImportsNothing_AndThrowsLimitMessage()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, _) = await CreateOwnerAndDatabaseAsync(dbContext, "quota-user");
        var importService = CreateDraftImportService(dbContext, quota: 10);

        using var reader = new StringReader(BuildPgnGames(15));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importService.ImportAsync(reader, ownerId, batchSize: 5));

        Assert.Contains("10", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await dbContext.StagingGames.CountAsync());
    }

    [Fact]
    public async Task DraftImport_NewImport_ClearsPreviousUnpromotedDraftData()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, _) = await CreateOwnerAndDatabaseAsync(dbContext, "replace-draft-user");
        var importService = CreateDraftImportService(dbContext);

        using (var firstReader = new StringReader(BuildPgnGames(4)))
        {
            var first = await importService.ImportAsync(firstReader, ownerId, batchSize: 2);
            Assert.Equal(4, first.ImportedCount);
            Assert.Equal(4, await dbContext.StagingGames.CountAsync());
        }

        using (var secondReader = new StringReader(BuildPgnGames(2)))
        {
            var second = await importService.ImportAsync(secondReader, ownerId, batchSize: 2);
            Assert.Equal(2, second.ImportedCount);

            Assert.Equal(2, await dbContext.StagingGames.CountAsync());
            Assert.All(await dbContext.StagingGames.AsNoTracking().ToListAsync(), game => Assert.Equal(ownerId, game.OwnerUserId));
        }
    }

    [Fact]
    public async Task HappyPath_ImportThenPromote_MovesRowsToMainTables()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, userDatabaseId) = await CreateOwnerAndDatabaseAsync(dbContext, "happy-user");

        var importService = CreateDraftImportService(dbContext);
        var promotionService = CreateDraftPromotionService(dbContext, new EfUnitOfWork(dbContext));

        using var reader = new StringReader(BuildPgnGames(10));
        var importResult = await importService.ImportAsync(reader, ownerId, batchSize: 5);

        var stagingBefore = await dbContext.StagingGames.CountAsync();
        Assert.Equal(10, stagingBefore);

        await promotionService.PromoteAsync(ownerId, userDatabaseId);

        Assert.Equal(0, await dbContext.StagingGames.CountAsync());
        Assert.Equal(0, await dbContext.StagingMoves.CountAsync());
        Assert.Equal(0, await dbContext.StagingPositions.CountAsync());

        Assert.Equal(10, await dbContext.Games.CountAsync());
        Assert.Equal(10, await dbContext.UserDatabaseGames.CountAsync());
        Assert.Equal(30, await dbContext.Positions.CountAsync());

        var links = await dbContext.UserDatabaseGames.ToListAsync();
        Assert.All(links, link => Assert.NotEqual(default, link.AddedAtUtc));
    }

    [Fact]
    public async Task AtomicTransaction_WhenPromotionFailsMidBatch_RollsBackMainWrites()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, userDatabaseId) = await CreateOwnerAndDatabaseAsync(dbContext, "atomic-user");

        var stagingGames = Enumerable.Range(1, 1000)
            .Select(i => new StagingGame
            {
                Id = Guid.NewGuid(),
                OwnerUserId = ownerId,
                CreatedAtUtc = DateTime.UtcNow,
                White = "Alpha",
                Black = "Beta",
                Result = "*",
                Pgn = "1. e4 e5 *",
                MoveCount = 1,
                Year = 2026,
                Event = "Atomic Event",
                Site = "Atomic Site",
                Round = i.ToString(),
                GameHash = $"atomic-hash-{i}",
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
                        Fen = "startpos",
                        FenHash = i,
                        PlyCount = 0,
                        SideToMove = 'w'
                    }
                ]
            })
            .ToArray();

        dbContext.StagingGames.AddRange(stagingGames);
        await dbContext.SaveChangesAsync();

        var baseRepository = new DraftPromotionRepository(dbContext);
        var failingRepository = new ThrowingDraftPromotionRepository(baseRepository);
        var unitOfWork = new EfUnitOfWork(dbContext);
        var promotionService = new DraftPromotionService(failingRepository, unitOfWork);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            promotionService.PromoteAsync(ownerId, userDatabaseId));

        Assert.Equal(0, await dbContext.Games.CountAsync());
        Assert.Equal(0, await dbContext.UserDatabaseGames.CountAsync());
        Assert.Equal(1000, await dbContext.StagingGames.CountAsync());
    }

    [Fact]
    public async Task TrackerBehavior_Promote1001Games_ClearsTrackerPerBatch()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, userDatabaseId) = await CreateOwnerAndDatabaseAsync(dbContext, "batch-user");

        var stagingGames = Enumerable.Range(1, 1001)
            .Select(i => new StagingGame
            {
                Id = Guid.NewGuid(),
                OwnerUserId = ownerId,
                CreatedAtUtc = DateTime.UtcNow,
                White = "Alpha",
                Black = "Beta",
                Result = "*",
                Pgn = "1. e4 e5 *",
                MoveCount = 1,
                Year = 2026,
                Event = "Batch Event",
                Site = "Batch Site",
                Round = i.ToString(),
                GameHash = $"hash-{i}",
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
                        Fen = "startpos",
                        FenHash = i,
                        PlyCount = 0,
                        SideToMove = 'w'
                    }
                ]
            })
            .ToArray();

        dbContext.StagingGames.AddRange(stagingGames);
        await dbContext.SaveChangesAsync();

        var countingUnitOfWork = new CountingUnitOfWork(dbContext);
        var promotionService = CreateDraftPromotionService(dbContext, countingUnitOfWork);

        var result = await promotionService.PromoteAsync(ownerId, userDatabaseId);

        Assert.Equal(1001, result.PromotedCount);
        Assert.Equal(1, countingUnitOfWork.ClearTrackerCalls);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        Assert.Equal(1001, await dbContext.Games.CountAsync());
        Assert.Equal(1001, await dbContext.UserDatabaseGames.CountAsync());
    }

    private static DraftImportService CreateDraftImportService(ChessXivDbContext dbContext, int quota = 200_000)
    {
        var parser = new PgnService();
        var serializer = new FenBoardStateSerializer();
        var factory = new BoardStateFactory(serializer);
        var transition = new BitboardBoardStateTransition();
        var hasher = new ZobristPositionHasher();
        var positionCoordinator = new PositionImportCoordinator(factory, serializer, transition, hasher);
        var repo = new DraftImportRepository(dbContext);
        var quotaService = new StubQuotaService(quota);
        var uow = new EfUnitOfWork(dbContext);

        return new DraftImportService(parser, positionCoordinator, repo, quotaService, uow);
    }

    private static DraftPromotionService CreateDraftPromotionService(ChessXivDbContext dbContext, IUnitOfWork unitOfWork)
    {
        var promotionRepo = new DraftPromotionRepository(dbContext);
        return new DraftPromotionService(promotionRepo, unitOfWork);
    }

    private static async Task<(string OwnerId, Guid UserDatabaseId)> CreateOwnerAndDatabaseAsync(ChessXivDbContext dbContext, string ownerId)
    {
        var user = new ApplicationUser
        {
            Id = ownerId,
            UserName = ownerId,
            Email = $"{ownerId}@example.com"
        };

        var database = new UserDatabase
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            Name = $"db-{ownerId}",
            IsPublic = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Users.Add(user);
        dbContext.UserDatabases.Add(database);
        await dbContext.SaveChangesAsync();

        return (ownerId, database.Id);
    }

    private static string BuildPgnGames(int count)
    {
        var openings = new (string White, string Black)[]
        {
            ("e4", "e5"),
            ("d4", "d5"),
            ("c4", "e5"),
            ("Nf3", "d5"),
            ("g3", "d5"),
            ("b3", "d5"),
            ("f4", "d5"),
            ("Nc3", "d5"),
            ("e3", "d5"),
            ("a3", "d5")
        };

        var blocks = Enumerable.Range(1, count)
            .Select(i =>
            {
                var opening = openings[(i - 1) % openings.Length];
                return
                $"[Event \"Draft Game {i}\"]\n" +
                "[Site \"Test\"]\n" +
                "[Date \"2026.03.18\"]\n" +
                $"[Round \"{i}\"]\n" +
                "[White \"Alpha\"]\n" +
                "[Black \"Beta\"]\n" +
                "[Result \"*\"]\n\n" +
                $"1. {opening.White} {opening.Black} *";
            });

        return string.Join("\n\n\n", blocks);
    }

    private sealed class CountingUnitOfWork(ChessXivDbContext dbContext) : IUnitOfWork
    {
        public int ClearTrackerCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            return new EfUnitOfWorkTransaction(tx);
        }

        public void ClearTracker()
        {
            ClearTrackerCalls++;
            dbContext.ChangeTracker.Clear();
        }
    }

    private sealed class ThrowingDraftPromotionRepository(IDraftPromotionRepository inner) : IDraftPromotionRepository
    {
        public Task<UserDatabase?> GetUserDatabaseAsync(Guid userDatabaseId, CancellationToken cancellationToken = default)
        {
            return inner.GetUserDatabaseAsync(userDatabaseId, cancellationToken);
        }

        public Task<int> PromoteAllAsync(
            string ownerUserId,
            Guid userDatabaseId,
            DateTime addedAtUtc,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated failure during promotion.");
        }

    }

    private sealed class StubQuotaService(int maxDraftImportGames) : IQuotaService
    {
        public Task<int> GetMaxDraftImportGamesAsync(string? ownerUserId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(maxDraftImportGames);
        }

        public Task<int> GetMaxSavedGamesAsync(string? ownerUserId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(10_000);
        }
    }
}
