using ChessXiv.Api.Authentication;
using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Api.Services;

public interface IDraftClaimService
{
    /// <summary>
    /// Reassigns an anonymous guest's staging games onto the account they just signed into.
    /// </summary>
    /// <remarks>
    /// A guest's draft is keyed by their throwaway token's subject. The frontend used to
    /// simply discard that token on login, which orphaned the draft forever - nobody could
    /// ever present that subject again, so the rows sat in the database, invisible to the
    /// user, until the idle sweep eventually caught up. This reassigns ownership instead, so
    /// signing in while mid-review of an import keeps the import.
    ///
    /// Declines rather than merging when the target account already has a draft of its own:
    /// merging two unrelated draft sets silently is more likely to surprise than help, and
    /// the guest rows are in no danger - they simply age out via the normal idle sweep like
    /// any other abandoned draft.
    /// </remarks>
    Task<ClaimGuestDraftResponse> ClaimAsync(string guestToken, string targetUserId, CancellationToken cancellationToken = default);
}

public sealed class DraftClaimService(
    ChessXivDbContext dbContext,
    IJwtTokenService jwtTokenService,
    IDraftSessionTracker draftSessionTracker) : IDraftClaimService
{
    public async Task<ClaimGuestDraftResponse> ClaimAsync(string guestToken, string targetUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(guestToken))
        {
            return new ClaimGuestDraftResponse(false, 0);
        }

        var guestUserId = jwtTokenService.TryGetGuestUserId(guestToken);
        if (guestUserId is null || string.Equals(guestUserId, targetUserId, StringComparison.Ordinal))
        {
            return new ClaimGuestDraftResponse(false, 0);
        }

        var alreadyHasDraft = await dbContext.StagingGames
            .AsNoTracking()
            .AnyAsync(g => g.OwnerUserId == targetUserId, cancellationToken);

        if (alreadyHasDraft)
        {
            return new ClaimGuestDraftResponse(false, 0);
        }

        var claimedCount = await dbContext.StagingGames
            .Where(g => g.OwnerUserId == guestUserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(g => g.OwnerUserId, targetUserId), cancellationToken);

        if (claimedCount > 0)
        {
            await draftSessionTracker.ClearAsync(guestUserId, cancellationToken);
            await draftSessionTracker.TouchAsync(targetUserId, cancellationToken);
        }

        return new ClaimGuestDraftResponse(claimedCount > 0, claimedCount);
    }
}
