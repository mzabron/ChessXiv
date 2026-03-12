using ChessBase.Application.Abstractions;
using ChessBase.Application.Abstractions.Repositories;
using ChessBase.Application.Contracts;
using ChessBase.Domain.Engine.Abstractions;

namespace ChessBase.Application.Services;

public class GameExplorerService(
    IGameExplorerRepository gameExplorerRepository,
    IPlayerRepository playerRepository,
    IBoardStateSerializer boardStateSerializer,
    IPositionHasher positionHasher) : IGameExplorerService
{
    public async Task<PagedResult<GameExplorerItemDto>> SearchAsync(GameExplorerSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Page = request.Page <= 0 ? 1 : request.Page;
        request.PageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 200);

        var normalizedWhiteFirstName = NormalizeOptional(request.WhiteFirstName);
        var normalizedWhiteLastName = NormalizeOptional(request.WhiteLastName);
        var normalizedBlackFirstName = NormalizeOptional(request.BlackFirstName);
        var normalizedBlackLastName = NormalizeOptional(request.BlackLastName);

        var hasWhiteFilter = normalizedWhiteFirstName is not null || normalizedWhiteLastName is not null;
        var hasBlackFilter = normalizedBlackFirstName is not null || normalizedBlackLastName is not null;

        var whitePlayerIds = hasWhiteFilter
            ? await playerRepository.SearchIdsAsync(normalizedWhiteFirstName, normalizedWhiteLastName, cancellationToken)
            : null;

        var blackPlayerIds = hasBlackFilter
            ? await playerRepository.SearchIdsAsync(normalizedBlackFirstName, normalizedBlackLastName, cancellationToken)
            : null;

        if (hasWhiteFilter && (whitePlayerIds is null || whitePlayerIds.Count == 0))
        {
            return new PagedResult<GameExplorerItemDto>();
        }

        if (hasBlackFilter && (blackPlayerIds is null || blackPlayerIds.Count == 0))
        {
            return new PagedResult<GameExplorerItemDto>();
        }

        var normalizedFen = request.SearchByPosition && !string.IsNullOrWhiteSpace(request.Fen)
            ? request.Fen.Trim()
            : null;

        if (request.SearchByPosition && request.PositionMode == PositionSearchMode.Subset && normalizedFen is null)
        {
            return new PagedResult<GameExplorerItemDto>();
        }

        long? fenHash = null;
        if (request.SearchByPosition && request.PositionMode == PositionSearchMode.Exact && normalizedFen is not null)
        {
            var state = boardStateSerializer.FromFen(normalizedFen);
            fenHash = unchecked((long)positionHasher.Compute(state));
        }

        if (request.SearchByPosition && request.PositionMode == PositionSearchMode.Exact && normalizedFen is null)
        {
            return new PagedResult<GameExplorerItemDto>();
        }

        return await gameExplorerRepository.SearchAsync(
            request,
            whitePlayerIds,
            blackPlayerIds,
            normalizedFen,
            fenHash,
            cancellationToken);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = PlayerNameNormalizer.Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}
