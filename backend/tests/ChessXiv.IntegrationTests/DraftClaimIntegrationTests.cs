using ChessXiv.Api.Authentication;
using ChessXiv.Api.Services;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Services;
using ChessXiv.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessXiv.IntegrationTests;

/// <summary>
/// Covers the guest-draft-to-account migration that runs on login. Before this existed, a
/// guest's staging games were orphaned the moment they signed in - the frontend discarded
/// the only token that could ever again prove ownership of them, so the rows became
/// permanently invisible while still consuming storage until the idle sweep eventually
/// caught up.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DraftClaimIntegrationTests(PostgresTestFixture fixture)
{
    private const string SigningKey = "integration-test-signing-key-at-least-32-bytes-long";

    [Fact]
    public async Task ClaimAsync_ReassignsStagingGames_FromGuestToTheSignedInAccount()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var tokenService = CreateJwtTokenService();
        var guestToken = tokenService.CreateGuestToken();
        var guestUserId = tokenService.TryGetGuestUserId(guestToken.AccessToken);
        Assert.NotNull(guestUserId);

        var targetUserId = await CreateUserAsync(dbContext, "claimant");
        await SeedStagingGameAsync(dbContext, guestUserId!, "createdAt: 10 minutes ago");

        var claimService = CreateClaimService(dbContext, tokenService);
        var result = await claimService.ClaimAsync(guestToken.AccessToken, targetUserId);

        Assert.True(result.Claimed);
        Assert.Equal(1, result.GameCount);

        dbContext.ChangeTracker.Clear();
        Assert.Equal(0, await dbContext.StagingGames.CountAsync(g => g.OwnerUserId == guestUserId));
        Assert.Equal(1, await dbContext.StagingGames.CountAsync(g => g.OwnerUserId == targetUserId));

        // The guest's session tracker row must move too, or the idle sweep would immediately
        // consider the (now correctly-owned) draft abandoned under its old identity.
        Assert.False(await dbContext.StagingDraftSessions.AnyAsync(s => s.OwnerUserId == guestUserId));
        Assert.True(await dbContext.StagingDraftSessions.AnyAsync(s => s.OwnerUserId == targetUserId));
    }

    [Fact]
    public async Task ClaimAsync_MigratesStagingPositions_AlongWithTheirGames()
    {
        // Positions are keyed by StagingGameId, not by owner, so they should simply follow
        // their game through the FK - this locks that assumption in.
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var tokenService = CreateJwtTokenService();
        var guestToken = tokenService.CreateGuestToken();
        var guestUserId = tokenService.TryGetGuestUserId(guestToken.AccessToken)!;
        var targetUserId = await CreateUserAsync(dbContext, "claimant-positions");

        var gameId = await SeedStagingGameAsync(dbContext, guestUserId, "with positions");
        dbContext.StagingPositions.Add(new StagingPosition
        {
            StagingGameId = gameId,
            PlyCount = 0,
            PosKey = [1, 2, 3],
            Result = GameResult.Unknown
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var claimService = CreateClaimService(dbContext, tokenService);
        await claimService.ClaimAsync(guestToken.AccessToken, targetUserId);

        dbContext.ChangeTracker.Clear();
        var migratedGame = await dbContext.StagingGames.SingleAsync(g => g.OwnerUserId == targetUserId);
        Assert.Equal(1, await dbContext.StagingPositions.CountAsync(p => p.StagingGameId == migratedGame.Id));
    }

    [Fact]
    public async Task ClaimAsync_DoesNothing_WhenTheAccountAlreadyHasADraft()
    {
        // Refuses to silently merge two unrelated draft sets - the guest rows are not lost,
        // they simply age out via the normal idle sweep instead.
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var tokenService = CreateJwtTokenService();
        var guestToken = tokenService.CreateGuestToken();
        var guestUserId = tokenService.TryGetGuestUserId(guestToken.AccessToken)!;
        var targetUserId = await CreateUserAsync(dbContext, "already-has-draft");

        await SeedStagingGameAsync(dbContext, guestUserId, "guest game");
        await SeedStagingGameAsync(dbContext, targetUserId, "already-existing game");

        var claimService = CreateClaimService(dbContext, tokenService);
        var result = await claimService.ClaimAsync(guestToken.AccessToken, targetUserId);

        Assert.False(result.Claimed);
        Assert.Equal(0, result.GameCount);

        dbContext.ChangeTracker.Clear();
        Assert.Equal(1, await dbContext.StagingGames.CountAsync(g => g.OwnerUserId == guestUserId));
        Assert.Equal(1, await dbContext.StagingGames.CountAsync(g => g.OwnerUserId == targetUserId));
    }

    [Fact]
    public async Task ClaimAsync_DoesNothing_WhenTheTokenIsNotAGuestToken()
    {
        // A forged or ordinary user token must not be able to pull another account's rows.
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var tokenService = CreateJwtTokenService();
        var targetUserId = await CreateUserAsync(dbContext, "victim-account");

        var otherUser = new ApplicationUser { Id = "attacker-id", UserName = "attacker", Email = "attacker@example.com" };
        var forgedToken = tokenService.CreateToken(otherUser);
        await SeedStagingGameAsync(dbContext, "attacker-id", "not a guest draft");

        var claimService = CreateClaimService(dbContext, tokenService);
        var result = await claimService.ClaimAsync(forgedToken.AccessToken, targetUserId);

        Assert.False(result.Claimed);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(1, await dbContext.StagingGames.CountAsync(g => g.OwnerUserId == "attacker-id"));
    }

    [Fact]
    public async Task ClaimAsync_DoesNothing_ForAGarbageToken()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var tokenService = CreateJwtTokenService();
        var targetUserId = await CreateUserAsync(dbContext, "garbage-token-user");

        var claimService = CreateClaimService(dbContext, tokenService);
        var result = await claimService.ClaimAsync("not-a-real-jwt", targetUserId);

        Assert.False(result.Claimed);
        Assert.Equal(0, result.GameCount);
    }

    private static JwtTokenService CreateJwtTokenService()
    {
        return new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "ChessXiv.Api",
            Audience = "ChessXiv.Web",
            SigningKey = SigningKey,
            ExpirationMinutes = 60
        }));
    }

    private static DraftClaimService CreateClaimService(ChessXivDbContext dbContext, IJwtTokenService tokenService)
    {
        return new DraftClaimService(dbContext, tokenService, new DraftSessionTracker(dbContext));
    }

    private static async Task<string> CreateUserAsync(ChessXivDbContext dbContext, string userName)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = userName,
            Email = $"{userName}@example.com"
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return user.Id;
    }

    private static async Task<Guid> SeedStagingGameAsync(ChessXivDbContext dbContext, string ownerUserId, string label)
    {
        var game = new StagingGame
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            CreatedAtUtc = DateTime.UtcNow,
            White = "Alpha",
            Black = "Beta",
            Result = "*",
            Pgn = $"1. e4 e5 * ; {label}",
            GameHash = $"hash-{Guid.NewGuid():N}"
        };

        dbContext.StagingGames.Add(game);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return game.Id;
    }
}
