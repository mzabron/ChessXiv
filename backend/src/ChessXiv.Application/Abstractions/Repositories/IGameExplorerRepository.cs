using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;

namespace ChessXiv.Application.Abstractions.Repositories;

public interface IGameExplorerRepository
{
    Task<UserDatabaseAccessStatus> GetUserDatabaseAccessStatusAsync(
        Guid userDatabaseId,
        string? ownerUserId,
        CancellationToken cancellationToken = default);

    Task<MoveTreeResponse> GetMoveTreeAsync(
        MoveTreeRequest request,
        string? ownerUserId,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName,
        byte[] posKey,
        PositionSearchTarget? filterTarget,
        CancellationToken cancellationToken = default);
}
