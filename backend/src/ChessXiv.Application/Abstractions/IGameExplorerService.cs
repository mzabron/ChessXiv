using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions;

public interface IGameExplorerService
{
    Task<PagedResult<GameExplorerItemDto>> SearchAsync(GameExplorerSearchRequest request, string? ownerUserId, CancellationToken cancellationToken = default);
    Task<MoveTreeResponse> GetMoveTreeAsync(MoveTreeRequest request, string ownerUserId, CancellationToken cancellationToken = default);
}
