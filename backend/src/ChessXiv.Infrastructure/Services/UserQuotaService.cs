using ChessXiv.Application.Abstractions;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Infrastructure.Services;

public sealed class UserQuotaService(ChessXivDbContext dbContext) : IQuotaService
{
    private const int FreeDraftImportMaxGames = 200_000;
    private const int PremiumDraftImportMaxGames = 500_000;

    public async Task<int> GetMaxDraftImportGamesAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        }

        var userTier = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == ownerUserId)
            .Select(u => u.UserTier)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(userTier, "Premium", StringComparison.OrdinalIgnoreCase))
        {
            return PremiumDraftImportMaxGames;
        }

        return FreeDraftImportMaxGames;
    }
}
