using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Services;

public sealed class DraftPromotionService(
    IDraftPromotionRepository draftPromotionRepository,
    IUnitOfWork unitOfWork) : IDraftPromotionService
{
    public async Task<DraftPromotionResult> PromoteAsync(
        string ownerUserId,
        Guid userDatabaseId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        }

        var userDatabase = await draftPromotionRepository.GetUserDatabaseAsync(userDatabaseId, cancellationToken);

        if (userDatabase is null)
        {
            throw new InvalidOperationException("Target user database was not found.");
        }

        if (!string.Equals(userDatabase.OwnerUserId, ownerUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Target user database does not belong to the current user.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var promotedCount = await draftPromotionRepository.PromoteAllAsync(
                ownerUserId,
                userDatabaseId,
                DateTime.UtcNow,
                cancellationToken);

            unitOfWork.ClearTracker();

            await transaction.CommitAsync(cancellationToken);
            return new DraftPromotionResult(promotedCount, 0);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
