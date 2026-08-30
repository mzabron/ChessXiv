using ChessXiv.Application.Abstractions;
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

/// <summary>
/// Covers removing selected games from a database, including the orphan cleanup that keeps
/// Games and the much larger Positions table from accumulating rows nothing references.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RemoveGamesIntegrationTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task RemovingAGameLinkedOnlyHere_DeletesTheGameAndItsPositions()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, databaseId) = await SeedDatabaseWithGamesAsync(dbContext, "solo-owner", gameCount: 2);

        var gameToRemove = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabaseId == databaseId)
            .Select(link => link.GameId)
            .FirstAsync();

        await RemoveGamesAsync(dbContext, databaseId, [gameToRemove]);

        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.UserDatabaseGames.AnyAsync(l => l.GameId == gameToRemove));
        Assert.False(await dbContext.Games.AnyAsync(g => g.Id == gameToRemove));
        // Positions cascade from Games, so none may survive their game.
        Assert.False(await dbContext.Positions.AnyAsync(p => p.GameId == gameToRemove));
        Assert.Equal(1, await dbContext.Games.CountAsync());
    }

    [Fact]
    public async Task RemovingAGameStillSavedElsewhere_KeepsTheGame()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (ownerId, firstDatabaseId) = await SeedDatabaseWithGamesAsync(dbContext, "shared-owner", gameCount: 1);

        var sharedGameId = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabaseId == firstDatabaseId)
            .Select(link => link.GameId)
            .FirstAsync();

        // The same game also lives in a second database.
        var secondDatabase = new UserDatabase
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            Name = "second-db",
            CreatedAtUtc = DateTime.UtcNow,
            ContentUpdatedAtUtc = DateTime.UtcNow
        };
        dbContext.UserDatabases.Add(secondDatabase);
        dbContext.UserDatabaseGames.Add(new UserDatabaseGame
        {
            UserDatabaseId = secondDatabase.Id,
            GameId = sharedGameId,
            AddedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await RemoveGamesAsync(dbContext, firstDatabaseId, [sharedGameId]);

        dbContext.ChangeTracker.Clear();
        Assert.True(await dbContext.Games.AnyAsync(g => g.Id == sharedGameId));
        Assert.True(await dbContext.Positions.AnyAsync(p => p.GameId == sharedGameId));
        Assert.False(await dbContext.UserDatabaseGames.AnyAsync(l => l.UserDatabaseId == firstDatabaseId));
        Assert.True(await dbContext.UserDatabaseGames.AnyAsync(l => l.UserDatabaseId == secondDatabase.Id));
    }

    [Fact]
    public async Task RemovingGames_UpdatesTheCountAndTheContentTimestamp()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var (_, databaseId) = await SeedDatabaseWithGamesAsync(dbContext, "counter-owner", gameCount: 3);

        var before = await dbContext.UserDatabases.AsNoTracking().SingleAsync(d => d.Id == databaseId);
        Assert.Equal(3, before.GameCount);

        var toRemove = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabaseId == databaseId)
            .Select(link => link.GameId)
            .Take(2)
            .ToArrayAsync();

        await RemoveGamesAsync(dbContext, databaseId, toRemove);

        dbContext.ChangeTracker.Clear();
        var after = await dbContext.UserDatabases.AsNoTracking().SingleAsync(d => d.Id == databaseId);

        Assert.Equal(1, after.GameCount);
        Assert.True(
            after.ContentUpdatedAtUtc > before.ContentUpdatedAtUtc,
            "Removing games must count as a content change.");
    }

    /// <summary>
    /// Mirrors what the controller does, without standing up the whole HTTP pipeline.
    /// </summary>
    private static async Task RemoveGamesAsync(ChessXivDbContext dbContext, Guid databaseId, Guid[] gameIds)
    {
        await dbContext.UserDatabaseGames
            .Where(link => link.UserDatabaseId == databaseId && gameIds.Contains(link.GameId))
            .ExecuteDeleteAsync();

        var orphanIds = await dbContext.Games
            .AsNoTracking()
            .Where(game => gameIds.Contains(game.Id) && !game.UserDatabaseGames.Any())
            .Select(game => game.Id)
            .ToArrayAsync();

        if (orphanIds.Length > 0)
        {
            await dbContext.Games.Where(g => orphanIds.Contains(g.Id)).ExecuteDeleteAsync();
        }

        await new DraftPromotionRepository(dbContext).SyncGameCountAsync(databaseId);
    }

    private static async Task<(string OwnerId, Guid DatabaseId)> SeedDatabaseWithGamesAsync(
        ChessXivDbContext dbContext,
        string ownerId,
        int gameCount)
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
            CreatedAtUtc = DateTime.UtcNow,
            ContentUpdatedAtUtc = DateTime.UtcNow
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

        for (var i = 0; i < gameCount; i++)
        {
            using var reader = new StringReader($"""
                [White "White{i}"]
                [Black "Black{i}"]
                [Result "1-0"]

                1. e4 e5 2. Nf3 1-0
                """);

            await importService.ImportToDatabaseAsync(reader, ownerId, database.Id);
        }

        dbContext.ChangeTracker.Clear();
        return (ownerId, database.Id);
    }
}
