using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Services;
using ChessXiv.IntegrationTests.Infrastructure;

namespace ChessXiv.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class UserQuotaServiceIntegrationTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task GetMaxDraftImportGamesAsync_ReturnsPremiumLimit_ForPremiumTier()
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

        var service = new UserQuotaService(dbContext);
        var max = await service.GetMaxDraftImportGamesAsync("premium-user");

        Assert.Equal(500_000, max);
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

        var service = new UserQuotaService(dbContext);
        var max = await service.GetMaxDraftImportGamesAsync("free-user");

        Assert.Equal(200_000, max);
    }

    [Fact]
    public async Task GetMaxDraftImportGamesAsync_ReturnsGuestSafeDefault_WhenOwnerIdIsNull()
    {
        await fixture.ResetDatabaseAsync();

        await using var dbContext = fixture.CreateDbContext();
        var service = new UserQuotaService(dbContext);

        var max = await service.GetMaxDraftImportGamesAsync(null);

        Assert.Equal(50_000, max);
    }
}
