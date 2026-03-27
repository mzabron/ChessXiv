using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions;

public interface IDraftPromotionService
{
    Task<DraftPromotionResult> PromoteAsync(
        string ownerUserId,
        Guid userDatabaseId,
        CancellationToken cancellationToken = default);
}
