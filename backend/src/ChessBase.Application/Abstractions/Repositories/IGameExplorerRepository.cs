using ChessBase.Application.Contracts;

namespace ChessBase.Application.Abstractions.Repositories;

public interface IGameExplorerRepository
{
    Task<PagedResult<GameExplorerItemDto>> SearchAsync(
        GameExplorerSearchRequest request,
        IReadOnlyCollection<Guid>? whitePlayerIds,
        IReadOnlyCollection<Guid>? blackPlayerIds,
        string? normalizedFen,
        long? fenHash,
        CancellationToken cancellationToken = default);
}
