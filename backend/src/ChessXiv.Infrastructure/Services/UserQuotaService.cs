using ChessXiv.Application.Abstractions;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ChessXiv.Infrastructure.Services;

public sealed class UserQuotaService(ChessXivDbContext dbContext, IMemoryCache memoryCache) : IQuotaService
{
    private static readonly TimeSpan UserTierCacheTtl = TimeSpan.FromMinutes(10);
    private const string PremiumTier = "Premium";

    private const int FreeDraftImportMaxGames = 200_000;
    private const int GuestDraftImportMaxGames = FreeDraftImportMaxGames;
    private const int PremiumDraftImportMaxGames = int.MaxValue;
    private const int FreeSavedGamesMaxGames = 10_000;
    private const int GuestSavedGamesMaxGames = FreeSavedGamesMaxGames;
    private const int PremiumSavedGamesMaxGames = int.MaxValue;

    public async Task<int> GetMaxDraftImportGamesAsync(string? ownerUserId, CancellationToken cancellationToken = default)
    {
        var isPremium = await IsPremiumUserAsync(ownerUserId, cancellationToken);
        if (isPremium)
        {
            return PremiumDraftImportMaxGames;
        }

        return string.IsNullOrWhiteSpace(ownerUserId)
            ? GuestDraftImportMaxGames
            : FreeDraftImportMaxGames;
    }

    public async Task<int> GetMaxSavedGamesAsync(string? ownerUserId, CancellationToken cancellationToken = default)
    {
        var isPremium = await IsPremiumUserAsync(ownerUserId, cancellationToken);
        if (isPremium)
        {
            return PremiumSavedGamesMaxGames;
        }

        return string.IsNullOrWhiteSpace(ownerUserId)
            ? GuestSavedGamesMaxGames
            : FreeSavedGamesMaxGames;
    }

    private async Task<bool> IsPremiumUserAsync(string? ownerUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return false;
        }

        var cacheKey = $"quota:user-tier:{ownerUserId}";
        if (!memoryCache.TryGetValue<string?>(cacheKey, out var userTier))
        {
            userTier = await dbContext.Users
                .AsNoTracking()
                .Where(u => u.Id == ownerUserId)
                .Select(u => u.UserTier)
                .FirstOrDefaultAsync(cancellationToken);

            memoryCache.Set(cacheKey, userTier, UserTierCacheTtl);
        }

        return string.Equals(userTier, PremiumTier, StringComparison.OrdinalIgnoreCase);
    }
}
