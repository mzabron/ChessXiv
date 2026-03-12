using ChessBase.Application.Contracts;

namespace ChessBase.Application.Abstractions;

public interface IGameExplorerService
{
    Task<PagedResult<GameExplorerItemDto>> SearchAsync(GameExplorerSearchRequest request, CancellationToken cancellationToken = default);
}
