using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Abstractions.Repositories;

public interface IDraftPromotionRepository
{
    Task<UserDatabase?> GetUserDatabaseAsync(Guid userDatabaseId, CancellationToken cancellationToken = default);
    Task<int> PromoteAllAsync(
        string ownerUserId,
        Guid userDatabaseId,
        DateTime addedAtUtc,
        CancellationToken cancellationToken = default);
}
