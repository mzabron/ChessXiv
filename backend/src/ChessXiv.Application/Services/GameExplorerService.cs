using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Exceptions;
using ChessXiv.Domain.Engine.Abstractions;

namespace ChessXiv.Application.Services;

public class GameExplorerService(
    IGameExplorerRepository gameExplorerRepository,
    IBoardStateSerializer boardStateSerializer,
    IPositionKeyCalculator positionKeyCalculator) : IGameExplorerService
{
    public async Task<MoveTreeResponse> GetMoveTreeAsync(
        MoveTreeRequest request,
        string? ownerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Source == MoveTreeSource.StagingSession && string.IsNullOrWhiteSpace(ownerUserId))
        {
            return new MoveTreeResponse();
        }

        if (request.Source == MoveTreeSource.UserDatabase
            && request.UserDatabaseId.HasValue
            && request.UserDatabaseId != Guid.Empty)
        {
            var accessStatus = await gameExplorerRepository.GetUserDatabaseAccessStatusAsync(
                request.UserDatabaseId.Value,
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

        if (!TryComputePositionKey(request.Fen, out var posKey))
        {
            return new MoveTreeResponse();
        }

        // The board position is always matched exactly; the mode applies to the optional
        // secondary "games that also reached position X" filter.
        PositionSearchTarget? filterTarget = null;
        if (request.SearchByPosition)
        {
            filterTarget = PositionSearchTarget.Resolve(
                true,
                request.FilterFen,
                boardStateSerializer,
                positionKeyCalculator);

            if (filterTarget is null)
            {
                return new MoveTreeResponse();
            }
        }

        request.MaxMoves = request.MaxMoves <= 0 ? 20 : Math.Min(request.MaxMoves, 100);

        var response = await gameExplorerRepository.GetMoveTreeAsync(
            request,
            ownerUserId,
            NormalizeOptional(request.WhiteFirstName),
            NormalizeOptional(request.WhiteLastName),
            NormalizeOptional(request.BlackFirstName),
            NormalizeOptional(request.BlackLastName),
            posKey!,
            filterTarget,
            cancellationToken);

        foreach (var move in response.Moves)
        {
            // Denominated by games that actually have a result, not by move.Games. Games
            // whose PGN result is "*" (unfinished, or simply missing from the tags) are
            // counted in Games but in none of the three buckets, so dividing by Games made
            // the three percentages sum to less than 100 - which is not a win ratio, and
            // showed up in the UI as an unexplained gap at the end of the win/draw bar.
            var decided = move.WhiteWins + move.Draws + move.BlackWins;
            if (decided <= 0)
            {
                continue;
            }

            move.WhiteWinPct = Math.Round(move.WhiteWins * 100m / decided, 2);
            move.DrawPct = Math.Round(move.Draws * 100m / decided, 2);
            move.BlackWinPct = Math.Round(move.BlackWins * 100m / decided, 2);
        }

        return response;
    }

    private bool TryComputePositionKey(string? fen, out byte[]? posKey)
    {
        posKey = null;
        var normalized = fen?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        try
        {
            posKey = positionKeyCalculator.Compute(boardStateSerializer.FromFen(normalized));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
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
