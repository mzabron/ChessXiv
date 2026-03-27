using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions.Repositories;

public interface IGameExplorerRepository
{
    Task<UserDatabaseAccessStatus> GetUserDatabaseAccessStatusAsync(
        Guid userDatabaseId,
        string? ownerUserId,
        CancellationToken cancellationToken = default);

    Task<PagedResult<GameExplorerItemDto>> SearchAsync(
        GameExplorerSearchRequest request,
        string? ownerUserId,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName,
        string? normalizedFen,
        long? fenHash,
        CancellationToken cancellationToken = default);

    Task<MoveTreeResponse> GetMoveTreeAsync(
        MoveTreeRequest request,
        string ownerUserId,
        string normalizedFen,
        long fenHash,
        CancellationToken cancellationToken = default);
}
