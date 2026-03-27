using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Exceptions;
using ChessXiv.Domain.Engine.Abstractions;

namespace ChessXiv.Application.Services;

public class GameExplorerService(
    IGameExplorerRepository gameExplorerRepository,
    IBoardStateSerializer boardStateSerializer,
    IPositionHasher positionHasher) : IGameExplorerService
{
    public async Task<PagedResult<GameExplorerItemDto>> SearchAsync(
        GameExplorerSearchRequest request,
        string? ownerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.UserDatabaseId.HasValue && request.UserDatabaseId != Guid.Empty)
        {
            var userDatabaseId = request.UserDatabaseId.Value;
            var accessStatus = await gameExplorerRepository.GetUserDatabaseAccessStatusAsync(
                userDatabaseId,
                ownerUserId,
                cancellationToken);

            if (accessStatus == UserDatabaseAccessStatus.NotFound)
            {
                throw new KeyNotFoundException("User database was not found.");
            }

            if (accessStatus == UserDatabaseAccessStatus.Forbidden)
            {
                throw new ForbiddenException("You do not have access to this user database.");
            }
        }

        request.Page = request.Page <= 0 ? 1 : request.Page;
        request.PageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 200);

        var normalizedWhiteFirstName = NormalizeOptional(request.WhiteFirstName);
        var normalizedWhiteLastName = NormalizeOptional(request.WhiteLastName);
        var normalizedBlackFirstName = NormalizeOptional(request.BlackFirstName);
        var normalizedBlackLastName = NormalizeOptional(request.BlackLastName);

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
            ownerUserId,
            normalizedWhiteFirstName,
            normalizedWhiteLastName,
            normalizedBlackFirstName,
            normalizedBlackLastName,
            normalizedFen,
            fenHash,
            cancellationToken);
    }

    public async Task<MoveTreeResponse> GetMoveTreeAsync(
        MoveTreeRequest request,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        }

        var normalizedFen = request.Fen?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedFen))
        {
            return new MoveTreeResponse();
        }

        request.MaxMoves = request.MaxMoves <= 0 ? 20 : Math.Min(request.MaxMoves, 100);

        var state = boardStateSerializer.FromFen(normalizedFen);
        var fenHash = unchecked((long)positionHasher.Compute(state));

        var response = await gameExplorerRepository.GetMoveTreeAsync(
            request,
            ownerUserId,
            normalizedFen,
            fenHash,
            cancellationToken);

        foreach (var move in response.Moves)
        {
            if (move.Games <= 0)
            {
                continue;
            }

            move.WhiteWinPct = Math.Round(move.WhiteWins * 100m / move.Games, 2);
            move.DrawPct = Math.Round(move.Draws * 100m / move.Games, 2);
            move.BlackWinPct = Math.Round(move.BlackWins * 100m / move.Games, 2);
        }

        return response;
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
