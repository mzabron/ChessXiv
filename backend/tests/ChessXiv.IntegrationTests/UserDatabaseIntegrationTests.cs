using ChessXiv.Domain.Entities;
using ChessXiv.IntegrationTests.Infrastructure;
using ChessXiv.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChessXiv.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class UserDatabaseIntegrationTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task AuthAndOwnerSync_RegisterUser_ThenCreateDatabase_OwnerIdMatchesUserId()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var userManager = CreateUserManager(dbContext);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = "owner_sync_user",
            Email = "owner.sync@example.com"
        };

        var createUserResult = await userManager.CreateAsync(user, "Password123!");
        Assert.True(createUserResult.Succeeded, string.Join(';', createUserResult.Errors.Select(e => e.Description)));

        var userDatabase = new UserDatabase
        {
            Id = Guid.NewGuid(),
            Name = "My Collection",
            IsPublic = false,
            OwnerUserId = user.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.UserDatabases.Add(userDatabase);
        await dbContext.SaveChangesAsync();

        var saved = await dbContext.UserDatabases.SingleAsync();
        Assert.Equal(user.Id, saved.OwnerUserId);
    }

    [Fact]
    public async Task ManyToManyLink_AddOneGameToTwoUserDatabases_CreatesTwoLinksWithoutGameDuplication()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();

        var ownerOne = new ApplicationUser
        {
            Id = "owner-1",
            UserName = "owner_one",
            Email = "owner.one@example.com"
        };

        var ownerTwo = new ApplicationUser
        {
            Id = "owner-2",
            UserName = "owner_two",
            Email = "owner.two@example.com"
        };

        var game = CreateGame("Alpha", "Beta");
        var dbOne = new UserDatabase
        {
            Id = Guid.NewGuid(),
            Name = "Db One",
            IsPublic = false,
            OwnerUserId = "owner-1",
            CreatedAtUtc = DateTime.UtcNow
        };

        var dbTwo = new UserDatabase
        {
            Id = Guid.NewGuid(),
            Name = "Db Two",
            IsPublic = true,
            OwnerUserId = "owner-2",
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Users.AddRange(ownerOne, ownerTwo);
        dbContext.Games.Add(game);
        dbContext.UserDatabases.AddRange(dbOne, dbTwo);

        dbContext.UserDatabaseGames.AddRange(
            new UserDatabaseGame
            {
                UserDatabaseId = dbOne.Id,
                GameId = game.Id,
                AddedAtUtc = DateTime.UtcNow
            },
            new UserDatabaseGame
            {
                UserDatabaseId = dbTwo.Id,
                GameId = game.Id,
                AddedAtUtc = DateTime.UtcNow
            });

        await dbContext.SaveChangesAsync();

        var linksCount = await dbContext.UserDatabaseGames.CountAsync();
        var gamesCount = await dbContext.Games.CountAsync();

        Assert.Equal(2, linksCount);
        Assert.Equal(1, gamesCount);

        var savedDbOne = await dbContext.UserDatabases
            .Include(d => d.UserDatabaseGames)
            .ThenInclude(udg => udg.Game)
            .FirstAsync(d => d.Id == dbOne.Id);

        Assert.Single(savedDbOne.UserDatabaseGames);
        Assert.Equal("Alpha", savedDbOne.UserDatabaseGames.First().Game.White);

        var savedDbTwo = await dbContext.UserDatabases
            .Include(d => d.UserDatabaseGames)
            .ThenInclude(udg => udg.Game)
            .FirstAsync(d => d.Id == dbTwo.Id);

        Assert.Single(savedDbTwo.UserDatabaseGames);
        Assert.Equal("Beta", savedDbTwo.UserDatabaseGames.First().Game.Black);
    }

    [Fact]
    public async Task DataIntegrity_DeleteUser_DeletesUserDatabasesAndLinks_ButKeepsGames()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var userManager = CreateUserManager(dbContext);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = "cascade_owner",
            Email = "cascade.owner@example.com"
        };

        var createUserResult = await userManager.CreateAsync(user, "Password123!");
        Assert.True(createUserResult.Succeeded, string.Join(';', createUserResult.Errors.Select(e => e.Description)));

        var game = CreateGame("Kasparov", "Karpov");
        var userDatabase = new UserDatabase
        {
            Id = Guid.NewGuid(),
            Name = "Owner Db",
            IsPublic = false,
            OwnerUserId = user.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Games.Add(game);
        dbContext.UserDatabases.Add(userDatabase);
        dbContext.UserDatabaseGames.Add(new UserDatabaseGame
        {
            UserDatabaseId = userDatabase.Id,
            GameId = game.Id,
            AddedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, await dbContext.UserDatabases.CountAsync());
        Assert.Equal(0, await dbContext.UserDatabaseGames.CountAsync());
        Assert.Equal(1, await dbContext.Games.CountAsync());
    }

    [Fact]
    public async Task Bookmarks_SameUserCanBookmarkSameDatabaseOnlyOnce()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();

        var owner = new ApplicationUser
        {
            Id = "owner-1",
            UserName = "owner_one",
            Email = "owner.one@example.com"
        };

        var viewer = new ApplicationUser
        {
            Id = "viewer-1",
            UserName = "viewer_one",
            Email = "viewer.one@example.com"
        };

        var userDatabase = new UserDatabase
        {
            Id = Guid.NewGuid(),
            Name = "Public Db",
            IsPublic = true,
            OwnerUserId = owner.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Users.AddRange(owner, viewer);
        dbContext.UserDatabases.Add(userDatabase);

        dbContext.UserDatabaseBookmarks.Add(new UserDatabaseBookmark
        {
            UserId = viewer.Id,
            UserDatabaseId = userDatabase.Id,
            CreatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        dbContext.UserDatabaseBookmarks.Add(new UserDatabaseBookmark
        {
            UserId = viewer.Id,
            UserDatabaseId = userDatabase.Id,
            CreatedAtUtc = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task DataIntegrity_DeleteUserDatabase_DeletesBookmarksForThatDatabase()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();

        var owner = new ApplicationUser
        {
            Id = "owner-1",
            UserName = "owner_one",
            Email = "owner.one@example.com"
        };

        var viewer = new ApplicationUser
        {
            Id = "viewer-1",
            UserName = "viewer_one",
            Email = "viewer.one@example.com"
        };

        var userDatabase = new UserDatabase
        {
            Id = Guid.NewGuid(),
            Name = "Public Db",
            IsPublic = true,
            OwnerUserId = owner.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Users.AddRange(owner, viewer);
        dbContext.UserDatabases.Add(userDatabase);
        dbContext.UserDatabaseBookmarks.Add(new UserDatabaseBookmark
        {
            UserId = viewer.Id,
            UserDatabaseId = userDatabase.Id,
            CreatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        dbContext.UserDatabases.Remove(userDatabase);
        await dbContext.SaveChangesAsync();

        Assert.Equal(0, await dbContext.UserDatabaseBookmarks.CountAsync());
    }

    private static UserManager<ApplicationUser> CreateUserManager(ChessXivDbContext dbContext)
    {
        return new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(dbContext),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new EmptyServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private static Game CreateGame(string white, string black)
    {
        var gameId = Guid.NewGuid();
        var whiteNormalized = white.ToLowerInvariant();
        var blackNormalized = black.ToLowerInvariant();

        return new Game
        {
            Id = gameId,
            White = white,
            Black = black,
            WhiteNormalizedFullName = whiteNormalized,
            BlackNormalizedFullName = blackNormalized,
            Result = "*",
            Pgn = "1. e4 e5 *",
            MoveCount = 1,
            Moves =
            [
                new Move
                {
                    Id = Guid.NewGuid(),
                    GameId = gameId,
                    MoveNumber = 1,
                    WhiteMove = "e4",
                    BlackMove = "e5"
                }
            ]
        };
    }
}
