using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Infrastructure.Repositories;

public class GameExplorerRepository(ChessXivDbContext dbContext) : IGameExplorerRepository
{
    public async Task<UserDatabaseAccessStatus> GetUserDatabaseAccessStatusAsync(
        Guid userDatabaseId,
        string? ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var dbAccess = await dbContext.UserDatabases
            .AsNoTracking()
            .Where(d => d.Id == userDatabaseId)
            .Select(d => new { d.OwnerUserId, d.IsPublic })
            .FirstOrDefaultAsync(cancellationToken);

        if (dbAccess is null)
        {
            return UserDatabaseAccessStatus.NotFound;
        }

        if (dbAccess.IsPublic)
        {
            return UserDatabaseAccessStatus.Accessible;
        }

        if (!string.IsNullOrWhiteSpace(ownerUserId)
            && string.Equals(dbAccess.OwnerUserId, ownerUserId, StringComparison.Ordinal))
        {
            return UserDatabaseAccessStatus.Accessible;
        }

        return UserDatabaseAccessStatus.Forbidden;
    }

    public Task<MoveTreeResponse> GetMoveTreeAsync(
        MoveTreeRequest request,
        string? ownerUserId,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName,
        byte[] posKey,
        PositionSearchTarget? filterTarget,
        CancellationToken cancellationToken = default)
    {
        return request.Source switch
        {
            MoveTreeSource.UserDatabase => GetUserDatabaseMoveTreeAsync(
                request,
                ownerUserId,
                normalizedWhiteFirstName,
                normalizedWhiteLastName,
                normalizedBlackFirstName,
                normalizedBlackLastName,
                posKey,
                filterTarget,
                cancellationToken),
            MoveTreeSource.StagingSession => GetStagingMoveTreeAsync(
                request,
                ownerUserId,
                normalizedWhiteFirstName,
                normalizedWhiteLastName,
                normalizedBlackFirstName,
                normalizedBlackLastName,
                posKey,
                filterTarget,
                cancellationToken),
            _ => Task.FromResult(new MoveTreeResponse())
        };
    }

    private async Task<MoveTreeResponse> GetUserDatabaseMoveTreeAsync(
        MoveTreeRequest request,
        string? ownerUserId,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName,
        byte[] posKey,
        PositionSearchTarget? filterTarget,
        CancellationToken cancellationToken)
    {
        if (!request.UserDatabaseId.HasValue || request.UserDatabaseId == Guid.Empty)
        {
            return new MoveTreeResponse();
        }

        var userDatabaseId = request.UserDatabaseId.Value;
        var accessStatus = await GetUserDatabaseAccessStatusAsync(userDatabaseId, ownerUserId, cancellationToken);
        if (accessStatus != UserDatabaseAccessStatus.Accessible)
        {
            return new MoveTreeResponse();
        }

        // Every position row carries the move played from it and the game's result, so the
        // continuations of a position are one index range - no self-join to ply + 1, and no
        // DISTINCT pass over (game, move, result) before grouping.
        var links = dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabaseId == userDatabaseId);

        if (HasFilters(request, normalizedWhiteFirstName, normalizedWhiteLastName, normalizedBlackFirstName, normalizedBlackLastName, filterTarget))
        {
            links = dbContext.UserDatabaseGames
                .AsNoTracking()
                .Where(link => link.UserDatabaseId == userDatabaseId)
                .ApplyPlayerFilters(
                    request.IgnoreColors,
                    normalizedWhiteFirstName,
                    normalizedWhiteLastName,
                    normalizedBlackFirstName,
                    normalizedBlackLastName)
                .ApplyScalarFilters(
                    request.EloEnabled,
                    request.EloFrom,
                    request.EloTo,
                    request.EloMode,
                    request.YearEnabled,
                    request.YearFrom,
                    request.YearTo,
                    request.EcoCode,
                    request.Result,
                    request.MoveCountFrom,
                    request.MoveCountTo)
                .ApplyPositionFilters(request.SearchByPosition, filterTarget?.PosKey, request.PositionMode, filterTarget?.PlyCount);
        }

        var rows = await (
            from position in dbContext.Positions.AsNoTracking()
            join link in links on position.GameId equals link.GameId
            where position.PosKey == posKey
            group position by new { position.NextMove, position.Result } into grouped
            select new { grouped.Key.NextMove, grouped.Key.Result, Count = grouped.Count() })
            .ToListAsync(cancellationToken);

        return BuildResponse(rows.Select(r => ((string?)r.NextMove, r.Result, r.Count)), request.MaxMoves);
    }

    private async Task<MoveTreeResponse> GetStagingMoveTreeAsync(
        MoveTreeRequest request,
        string? ownerUserId,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName,
        byte[] posKey,
        PositionSearchTarget? filterTarget,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return new MoveTreeResponse();
        }

        var games = dbContext.StagingGames
            .AsNoTracking()
            .Where(game => game.OwnerUserId == ownerUserId)
            .ApplyPlayerFilters(
                request.IgnoreColors,
                normalizedWhiteFirstName,
                normalizedWhiteLastName,
                normalizedBlackFirstName,
                normalizedBlackLastName)
            .ApplyScalarFilters(
                request.EloEnabled,
                request.EloFrom,
                request.EloTo,
                request.EloMode,
                request.YearEnabled,
                request.YearFrom,
                request.YearTo,
                request.EcoCode,
                request.Result,
                request.MoveCountFrom,
                request.MoveCountTo)
            .ApplyPositionFilters(request.SearchByPosition, filterTarget?.PosKey, request.PositionMode, filterTarget?.PlyCount);

        var rows = await (
            from position in dbContext.StagingPositions.AsNoTracking()
            join game in games on position.StagingGameId equals game.Id
            where position.PosKey == posKey
            group position by new { position.NextMove, position.Result } into grouped
            select new { grouped.Key.NextMove, grouped.Key.Result, Count = grouped.Count() })
            .ToListAsync(cancellationToken);

        return BuildResponse(rows.Select(r => ((string?)r.NextMove, r.Result, r.Count)), request.MaxMoves);
    }

    /// <summary>
    /// Turns per-(move, result) counts into the response. The total number of games in the
    /// position falls out of the same rows, including the games that ended there (NextMove
    /// null), so it no longer needs its own DISTINCT count query.
    /// </summary>
    private static MoveTreeResponse BuildResponse(
        IEnumerable<(string? NextMove, GameResult Result, int Count)> rows,
        int maxMoves)
    {
        var totalGamesInPosition = 0;
        var byMove = new Dictionary<string, MoveTreeAggregate>(StringComparer.Ordinal);

        foreach (var (nextMove, result, count) in rows)
        {
            totalGamesInPosition += count;

            if (string.IsNullOrEmpty(nextMove))
            {
                continue;
            }

            if (!byMove.TryGetValue(nextMove, out var aggregate))
            {
                aggregate = new MoveTreeAggregate { MoveSan = nextMove };
                byMove[nextMove] = aggregate;
            }

            aggregate.Games += count;

            switch (result)
            {
                case GameResult.WhiteWin:
                    aggregate.WhiteWins += count;
                    break;
                case GameResult.Draw:
                    aggregate.Draws += count;
                    break;
                case GameResult.BlackWin:
                    aggregate.BlackWins += count;
                    break;
            }
        }

        return new MoveTreeResponse
        {
            TotalGamesInPosition = totalGamesInPosition,
            Moves = byMove.Values
                .OrderByDescending(x => x.Games)
                .ThenBy(x => x.MoveSan, StringComparer.Ordinal)
                .Take(maxMoves)
                .Select(ToMoveDto)
                .ToArray()
        };
    }

    private static bool HasFilters(
        MoveTreeRequest request,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName,
        PositionSearchTarget? filterTarget)
    {
        return normalizedWhiteFirstName is not null
            || normalizedWhiteLastName is not null
            || normalizedBlackFirstName is not null
            || normalizedBlackLastName is not null
            || request.EloEnabled
            || request.YearEnabled
            || !string.IsNullOrWhiteSpace(request.EcoCode)
            || !string.IsNullOrWhiteSpace(request.Result)
            || request.MoveCountFrom.HasValue
            || request.MoveCountTo.HasValue
            || (request.SearchByPosition && filterTarget is not null);
    }

    private static MoveTreeMoveDto ToMoveDto(MoveTreeAggregate aggregate)
    {
        return new MoveTreeMoveDto
        {
            MoveSan = aggregate.MoveSan,
            Games = aggregate.Games,
            WhiteWins = aggregate.WhiteWins,
            Draws = aggregate.Draws,
            BlackWins = aggregate.BlackWins
        };
    }


    private sealed class MoveTreeAggregate
    {
        public string MoveSan { get; set; } = string.Empty;
        public int Games { get; set; }
        public int WhiteWins { get; set; }
        public int Draws { get; set; }
        public int BlackWins { get; set; }
    }
}
