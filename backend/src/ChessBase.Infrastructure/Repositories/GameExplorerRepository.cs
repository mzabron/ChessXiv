using ChessBase.Application.Abstractions.Repositories;
using ChessBase.Application.Contracts;
using ChessBase.Domain.Entities;
using ChessBase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessBase.Infrastructure.Repositories;

public class GameExplorerRepository(ChessBaseDbContext dbContext) : IGameExplorerRepository
{
    public async Task<PagedResult<GameExplorerItemDto>> SearchAsync(
        GameExplorerSearchRequest request,
        IReadOnlyCollection<Guid>? whitePlayerIds,
        IReadOnlyCollection<Guid>? blackPlayerIds,
        string? normalizedFen,
        long? fenHash,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Games.AsNoTracking().AsQueryable();

        query = ApplyPlayerFilters(query, request.IgnoreColors, whitePlayerIds, blackPlayerIds);
        query = ApplyScalarFilters(query, request);
        query = ApplyPositionFilters(query, request, normalizedFen, fenHash);

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

    private static IQueryable<Game> ApplyScalarFilters(IQueryable<Game> query, GameExplorerSearchRequest request)
    {
        if (request.EloEnabled && request.EloFrom.HasValue && request.EloTo.HasValue)
        {
            var from = request.EloFrom.Value;
            var to = request.EloTo.Value;

            query = request.EloMode switch
            {
                EloFilterMode.One => query.Where(g =>
                    (g.WhiteElo.HasValue && g.WhiteElo.Value >= from && g.WhiteElo.Value <= to) ||
                    (g.BlackElo.HasValue && g.BlackElo.Value >= from && g.BlackElo.Value <= to)),
                EloFilterMode.Both => query.Where(g =>
                    g.WhiteElo.HasValue && g.BlackElo.HasValue &&
                    g.WhiteElo.Value >= from && g.WhiteElo.Value <= to &&
                    g.BlackElo.Value >= from && g.BlackElo.Value <= to),
                EloFilterMode.Avg => query.Where(g =>
                    g.WhiteElo.HasValue && g.BlackElo.HasValue &&
                    ((g.WhiteElo.Value + g.BlackElo.Value) / 2.0) >= from &&
                    ((g.WhiteElo.Value + g.BlackElo.Value) / 2.0) <= to),
                _ => query
            };
        }

        if (request.YearEnabled && request.YearFrom.HasValue && request.YearTo.HasValue)
        {
            var from = request.YearFrom.Value;
            var to = request.YearTo.Value;
            query = query.Where(g => g.Year >= from && g.Year <= to);
        }

        if (!string.IsNullOrWhiteSpace(request.EcoCode))
        {
            var eco = request.EcoCode.Trim().ToUpperInvariant();
            query = query.Where(g => g.ECO != null && g.ECO.StartsWith(eco));
        }

        if (!string.IsNullOrWhiteSpace(request.Result))
        {
            var result = request.Result.Trim();
            query = query.Where(g => g.Result == result);
        }

        if (request.MoveCountFrom.HasValue)
        {
            var from = request.MoveCountFrom.Value;
            query = query.Where(g => g.MoveCount >= from);
        }

        if (request.MoveCountTo.HasValue)
        {
            var to = request.MoveCountTo.Value;
            query = query.Where(g => g.MoveCount <= to);
        }

        return query;
    }

    private static IQueryable<Game> ApplyPlayerFilters(
        IQueryable<Game> query,
        bool ignoreColors,
        IReadOnlyCollection<Guid>? whitePlayerIds,
        IReadOnlyCollection<Guid>? blackPlayerIds)
    {
        var hasWhite = whitePlayerIds is { Count: > 0 };
        var hasBlack = blackPlayerIds is { Count: > 0 };

        if (!hasWhite && !hasBlack)
        {
            return query;
        }

        if (!ignoreColors)
        {
            if (hasWhite)
            {
                query = query.Where(g => g.WhitePlayerId.HasValue && whitePlayerIds!.Contains(g.WhitePlayerId.Value));
            }

            if (hasBlack)
            {
                query = query.Where(g => g.BlackPlayerId.HasValue && blackPlayerIds!.Contains(g.BlackPlayerId.Value));
            }

            return query;
        }

        if (hasWhite && hasBlack)
        {
            var whiteIds = whitePlayerIds!;
            var blackIds = blackPlayerIds!;
            query = query.Where(g =>
                (g.WhitePlayerId.HasValue && g.BlackPlayerId.HasValue && whiteIds.Contains(g.WhitePlayerId.Value) && blackIds.Contains(g.BlackPlayerId.Value)) ||
                (g.WhitePlayerId.HasValue && g.BlackPlayerId.HasValue && blackIds.Contains(g.WhitePlayerId.Value) && whiteIds.Contains(g.BlackPlayerId.Value)));
            return query;
        }

        var ids = hasWhite ? whitePlayerIds! : blackPlayerIds!;
        return query.Where(g =>
            (g.WhitePlayerId.HasValue && ids.Contains(g.WhitePlayerId.Value)) ||
            (g.BlackPlayerId.HasValue && ids.Contains(g.BlackPlayerId.Value)));
    }

    private static IQueryable<Game> ApplyPositionFilters(
        IQueryable<Game> query,
        GameExplorerSearchRequest request,
        string? normalizedFen,
        long? fenHash)
    {
        if (!request.SearchByPosition)
        {
            return query;
        }

        if (request.PositionMode == PositionSearchMode.Exact)
        {
            if (fenHash.HasValue)
            {
                var hash = fenHash.Value;
                if (!string.IsNullOrWhiteSpace(normalizedFen))
                {
                    query = query.Where(g => g.Positions.Any(p => p.FenHash == hash && p.Fen == normalizedFen));
                }
                else
                {
                    query = query.Where(g => g.Positions.Any(p => p.FenHash == hash));
                }
            }

            return query;
        }

        var piecePlacement = ExtractPiecePlacement(normalizedFen);
        if (string.IsNullOrWhiteSpace(piecePlacement))
        {
            return query;
        }

        var likePattern = piecePlacement + " %";
        return query.Where(g => g.Positions.Any(p => EF.Functions.Like(p.Fen, likePattern)));
    }

    private static string? ExtractPiecePlacement(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            return null;
        }

        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 6 ? parts[0] : null;
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
}
