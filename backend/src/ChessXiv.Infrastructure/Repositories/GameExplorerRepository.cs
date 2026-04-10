using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
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

    public async Task<PagedResult<GameExplorerItemDto>> SearchAsync(
        GameExplorerSearchRequest request,
        string? ownerUserId,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName,
        string? normalizedFen,
        long? fenHash,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Game> query;

        if (request.UserDatabaseId.HasValue && request.UserDatabaseId != Guid.Empty)
        {
            var userDatabaseId = request.UserDatabaseId.Value;
            var accessStatus = await GetUserDatabaseAccessStatusAsync(userDatabaseId, ownerUserId, cancellationToken);
            if (accessStatus != UserDatabaseAccessStatus.Accessible)
            {
                return new PagedResult<GameExplorerItemDto>();
            }

            query =
                from link in dbContext.UserDatabaseGames.AsNoTracking()
                join game in dbContext.Games.AsNoTracking() on link.GameId equals game.Id
                where link.UserDatabaseId == userDatabaseId
                select game;
        }
        else
        {
            var hasCurrentUserId = !string.IsNullOrWhiteSpace(ownerUserId);
            query = dbContext.Games
                .AsNoTracking()
                .Where(game => game.UserDatabaseGames.Any(link =>
                    link.UserDatabase.IsPublic
                    || (hasCurrentUserId && link.UserDatabase.OwnerUserId == ownerUserId)));
        }

        query = query.ApplyPlayerFilters(
            request.IgnoreColors,
            normalizedWhiteFirstName,
            normalizedWhiteLastName,
            normalizedBlackFirstName,
            normalizedBlackLastName);
        query = query.ApplyScalarFilters(request);
        query = query.ApplyPositionFilters(request.SearchByPosition, normalizedFen, fenHash, request.PositionMode);

        var totalCount = await query.CountAsync(cancellationToken);

        query = ApplySorting(query, request.SortBy, request.SortDirection);

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 200);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new GameExplorerItemDto
            {
                GameId = g.Id,
                Year = g.Year > 0 ? g.Year : null,
                White = g.White,
                WhiteElo = g.WhiteElo,
                Result = g.Result,
                Black = g.Black,
                BlackElo = g.BlackElo,
                Eco = g.ECO,
                Tournament = g.Event,
                MoveCount = g.MoveCount
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<GameExplorerItemDto>
        {
            TotalCount = totalCount,
            Items = items
        };
    }

    public Task<MoveTreeResponse> GetMoveTreeAsync(
        MoveTreeRequest request,
        string ownerUserId,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName,
        string normalizedFen,
        long fenHash,
        string? normalizedFilterFen,
        long? filterFenHash,
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
                normalizedFen,
                fenHash,
                normalizedFilterFen,
                filterFenHash,
                cancellationToken),
            MoveTreeSource.StagingSession => GetStagingMoveTreeAsync(
                request,
                ownerUserId,
                normalizedWhiteFirstName,
                normalizedWhiteLastName,
                normalizedBlackFirstName,
                normalizedBlackLastName,
                normalizedFen,
                fenHash,
                normalizedFilterFen,
                filterFenHash,
                cancellationToken),
            _ => Task.FromResult(new MoveTreeResponse())
        };
    }

    private async Task<MoveTreeResponse> GetUserDatabaseMoveTreeAsync(
        MoveTreeRequest request,
        string ownerUserId,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName,
        string normalizedFen,
        long fenHash,
        string? normalizedFilterFen,
        long? filterFenHash,
        CancellationToken cancellationToken)
    {
        if (!request.UserDatabaseId.HasValue || request.UserDatabaseId == Guid.Empty)
        {
            return new MoveTreeResponse();
        }

        var userDatabaseId = request.UserDatabaseId.Value;
        var hasAccess = await dbContext.UserDatabases
            .AsNoTracking()
            .AnyAsync(d => d.Id == userDatabaseId && d.OwnerUserId == ownerUserId, cancellationToken);

        if (!hasAccess)
        {
            return new MoveTreeResponse();
        }

        var filteredGames = dbContext.UserDatabaseGames
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
            .ApplyPositionFilters(request.SearchByPosition, normalizedFilterFen, filterFenHash, request.PositionMode);

        var parentPositions =
            from link in filteredGames
            join parent in dbContext.Positions.AsNoTracking() on link.GameId equals parent.GameId
            where parent.FenHash == fenHash && parent.Fen == normalizedFen
            select new
            {
                parent.GameId,
                parent.PlyCount,
                link.Game.Result
            };

        var totalGamesInPosition = await parentPositions
            .Select(p => p.GameId)
            .Distinct()
            .CountAsync(cancellationToken);

        var aggregates = await (
            from parent in parentPositions
            join child in dbContext.Positions.AsNoTracking()
                on new { parent.GameId, NextPly = parent.PlyCount + 1 }
                equals new { child.GameId, NextPly = child.PlyCount }
            where child.LastMove != null && child.LastMove != string.Empty
            select new
            {
                parent.GameId,
                MoveSan = child.LastMove!,
                parent.Result
            })
            .Distinct()
            .GroupBy(x => x.MoveSan)
            .Select(g => new MoveTreeAggregate
            {
                MoveSan = g.Key,
                Games = g.Count(),
                WhiteWins = g.Count(x => x.Result == "1-0"),
                Draws = g.Count(x => x.Result == "1/2-1/2"),
                BlackWins = g.Count(x => x.Result == "0-1")
            })
            .OrderByDescending(x => x.Games)
            .ThenBy(x => x.MoveSan)
            .Take(request.MaxMoves)
            .ToListAsync(cancellationToken);

        return new MoveTreeResponse
        {
            TotalGamesInPosition = totalGamesInPosition,
            Moves = aggregates
                .Select(ToMoveDto)
                .ToArray()
        };
    }

    private async Task<MoveTreeResponse> GetStagingMoveTreeAsync(
        MoveTreeRequest request,
        string ownerUserId,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName,
        string normalizedFen,
        long fenHash,
        string? normalizedFilterFen,
        long? filterFenHash,
        CancellationToken cancellationToken)
    {
        var filteredGames = dbContext.StagingGames
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
            .ApplyPositionFilters(request.SearchByPosition, normalizedFilterFen, filterFenHash, request.PositionMode);

        var parentPositions =
            from game in filteredGames
            join parent in dbContext.StagingPositions.AsNoTracking() on game.Id equals parent.StagingGameId
            where parent.FenHash == fenHash
                  && parent.Fen == normalizedFen
            select new
            {
                parent.StagingGameId,
                parent.PlyCount,
                game.Result
            };

        var totalGamesInPosition = await parentPositions
            .Select(p => p.StagingGameId)
            .Distinct()
            .CountAsync(cancellationToken);

        var aggregates = await (
            from parent in parentPositions
            join child in dbContext.StagingPositions.AsNoTracking()
                on new { StagingGameId = parent.StagingGameId, NextPly = parent.PlyCount + 1 }
                equals new { child.StagingGameId, NextPly = child.PlyCount }
            where child.LastMove != null && child.LastMove != string.Empty
            select new
            {
                parent.StagingGameId,
                MoveSan = child.LastMove!,
                parent.Result
            })
            .Distinct()
            .GroupBy(x => x.MoveSan)
            .Select(g => new MoveTreeAggregate
            {
                MoveSan = g.Key,
                Games = g.Count(),
                WhiteWins = g.Count(x => x.Result == "1-0"),
                Draws = g.Count(x => x.Result == "1/2-1/2"),
                BlackWins = g.Count(x => x.Result == "0-1")
            })
            .OrderByDescending(x => x.Games)
            .ThenBy(x => x.MoveSan)
            .Take(request.MaxMoves)
            .ToListAsync(cancellationToken);

        return new MoveTreeResponse
        {
            TotalGamesInPosition = totalGamesInPosition,
            Moves = aggregates
                .Select(ToMoveDto)
                .ToArray()
        };
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

    private static IQueryable<Game> ApplySorting(IQueryable<Game> query, GameExplorerSortBy sortBy, SortDirection sortDirection)
    {
        var desc = sortDirection == SortDirection.Desc;

        return (sortBy, desc) switch
        {
            (GameExplorerSortBy.Year, true) => query.OrderByDescending(g => g.Year).ThenBy(g => g.Id),
            (GameExplorerSortBy.Year, false) => query.OrderBy(g => g.Year).ThenBy(g => g.Id),

            (GameExplorerSortBy.White, true) => query.OrderByDescending(g => g.White).ThenBy(g => g.Id),
            (GameExplorerSortBy.White, false) => query.OrderBy(g => g.White).ThenBy(g => g.Id),

            (GameExplorerSortBy.WhiteElo, true) => query.OrderByDescending(g => g.WhiteElo).ThenBy(g => g.Id),
            (GameExplorerSortBy.WhiteElo, false) => query.OrderBy(g => g.WhiteElo).ThenBy(g => g.Id),

            (GameExplorerSortBy.Result, true) => query.OrderByDescending(g => g.Result).ThenBy(g => g.Id),
            (GameExplorerSortBy.Result, false) => query.OrderBy(g => g.Result).ThenBy(g => g.Id),

            (GameExplorerSortBy.Black, true) => query.OrderByDescending(g => g.Black).ThenBy(g => g.Id),
            (GameExplorerSortBy.Black, false) => query.OrderBy(g => g.Black).ThenBy(g => g.Id),

            (GameExplorerSortBy.BlackElo, true) => query.OrderByDescending(g => g.BlackElo).ThenBy(g => g.Id),
            (GameExplorerSortBy.BlackElo, false) => query.OrderBy(g => g.BlackElo).ThenBy(g => g.Id),

            (GameExplorerSortBy.Eco, true) => query.OrderByDescending(g => g.ECO).ThenBy(g => g.Id),
            (GameExplorerSortBy.Eco, false) => query.OrderBy(g => g.ECO).ThenBy(g => g.Id),

            (GameExplorerSortBy.Event, true) => query.OrderByDescending(g => g.Event).ThenBy(g => g.Id),
            (GameExplorerSortBy.Event, false) => query.OrderBy(g => g.Event).ThenBy(g => g.Id),

            (GameExplorerSortBy.MoveCount, true) => query.OrderByDescending(g => g.MoveCount).ThenBy(g => g.Id),
            (GameExplorerSortBy.MoveCount, false) => query.OrderBy(g => g.MoveCount).ThenBy(g => g.Id),

            _ => query.OrderByDescending(g => g.Year).ThenBy(g => g.Id)
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
