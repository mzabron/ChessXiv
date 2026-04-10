using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Services;
using ChessXiv.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace ChessXiv.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class UserQuotaServiceIntegrationTests(PostgresTestFixture fixture)
{
    private static MemoryCache CreateCache()
    {
        return new MemoryCache(new MemoryCacheOptions());
    }

    [Fact]
    public async Task GetMaxDraftImportGamesAsync_ReturnsUnlimited_ForPremiumTier()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(new ApplicationUser
        {
            Id = "premium-user",
            UserName = "premium-user",
            Email = "premium@example.com",
            UserTier = "Premium"
        });
        await dbContext.SaveChangesAsync();

        using var cache = CreateCache();
        var service = new UserQuotaService(dbContext, cache);
        var max = await service.GetMaxDraftImportGamesAsync("premium-user");

        Assert.Equal(int.MaxValue, max);
    }

    [Fact]
    public async Task GetMaxDraftImportGamesAsync_ReturnsFreeLimit_ForFreeTier()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(new ApplicationUser
        {
            Id = "free-user",
            UserName = "free-user",
            Email = "free@example.com",
            UserTier = "Free"
        });
        await dbContext.SaveChangesAsync();

        using var cache = CreateCache();
        var service = new UserQuotaService(dbContext, cache);
        var max = await service.GetMaxDraftImportGamesAsync("free-user");

        Assert.Equal(200_000, max);
    }

    [Fact]
    public async Task GetMaxDraftImportGamesAsync_ReturnsGuestSafeDefault_WhenOwnerIdIsNull()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        using var cache = CreateCache();
        var service = new UserQuotaService(dbContext, cache);

        var max = await service.GetMaxDraftImportGamesAsync(null);

        Assert.Equal(200_000, max);
    }

    [Fact]
    public async Task GetMaxSavedGamesAsync_ReturnsFreeLimit_ForFreeTier()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(new ApplicationUser
        {
            Id = "saved-free-user",
            UserName = "saved-free-user",
            Email = "saved-free@example.com",
            UserTier = "Free"
        });
        await dbContext.SaveChangesAsync();

        using var cache = CreateCache();
        var service = new UserQuotaService(dbContext, cache);
        var max = await service.GetMaxSavedGamesAsync("saved-free-user");

        Assert.Equal(10_000, max);
    }
}
