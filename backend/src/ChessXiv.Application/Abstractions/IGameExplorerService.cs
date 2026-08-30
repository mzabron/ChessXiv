using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions;

public interface IGameExplorerService
{
    Task<MoveTreeResponse> GetMoveTreeAsync(MoveTreeRequest request, string? ownerUserId, CancellationToken cancellationToken = default);
}
