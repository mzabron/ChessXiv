using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Entities;

namespace ChessXiv.Infrastructure.Repositories;

public static class GameFilteringExtensions
{
    public static IQueryable<Game> ApplyScalarFilters(this IQueryable<Game> query, GameExplorerSearchRequest request)
    {
        return query.ApplyScalarFilters(
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
            request.MoveCountTo);
    }

    public static IQueryable<Game> ApplyScalarFilters(
        this IQueryable<Game> query,
        bool eloEnabled,
        int? eloFrom,
        int? eloTo,
        EloFilterMode eloMode,
        bool yearEnabled,
        int? yearFrom,
        int? yearTo,
        string? ecoCode,
        string? result,
        int? moveCountFrom,
        int? moveCountTo)
    {
        if (eloEnabled && eloFrom.HasValue && eloTo.HasValue)
        {
            var from = eloFrom.Value;
            var to = eloTo.Value;

            query = eloMode switch
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

        if (yearEnabled && yearFrom.HasValue && yearTo.HasValue)
        {
            var from = yearFrom.Value;
            var to = yearTo.Value;
            query = query.Where(g => g.Year >= from && g.Year <= to);
        }

        if (!string.IsNullOrWhiteSpace(ecoCode))
        {
            var normalizedEcoCode = ecoCode.Trim().ToUpperInvariant();
            query = query.Where(g => g.ECO != null && g.ECO.StartsWith(normalizedEcoCode));
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            var normalizedResult = result.Trim();
            query = query.Where(g => g.Result == normalizedResult);
        }

        if (moveCountFrom.HasValue)
        {
            var from = moveCountFrom.Value;
            query = query.Where(g => g.MoveCount >= from);
        }

        if (moveCountTo.HasValue)
        {
            var to = moveCountTo.Value;
            query = query.Where(g => g.MoveCount <= to);
        }

        return query;
    }

    public static IQueryable<StagingGame> ApplyScalarFilters(
        this IQueryable<StagingGame> query,
        bool eloEnabled,
        int? eloFrom,
        int? eloTo,
        EloFilterMode eloMode,
        bool yearEnabled,
        int? yearFrom,
        int? yearTo,
        string? ecoCode,
        string? result,
        int? moveCountFrom,
        int? moveCountTo)
    {
        if (eloEnabled && eloFrom.HasValue && eloTo.HasValue)
        {
            var from = eloFrom.Value;
            var to = eloTo.Value;

            query = eloMode switch
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

        if (yearEnabled && yearFrom.HasValue && yearTo.HasValue)
        {
            var from = yearFrom.Value;
            var to = yearTo.Value;
            query = query.Where(g => g.Year >= from && g.Year <= to);
        }

        if (!string.IsNullOrWhiteSpace(ecoCode))
        {
            var normalizedEcoCode = ecoCode.Trim().ToUpperInvariant();
            query = query.Where(g => g.ECO != null && g.ECO.StartsWith(normalizedEcoCode));
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            var normalizedResult = result.Trim();
            query = query.Where(g => g.Result == normalizedResult);
        }

        if (moveCountFrom.HasValue)
        {
            var from = moveCountFrom.Value;
            query = query.Where(g => g.MoveCount >= from);
        }

        if (moveCountTo.HasValue)
        {
            var to = moveCountTo.Value;
            query = query.Where(g => g.MoveCount <= to);
        }

        return query;
    }

    public static IQueryable<UserDatabaseGame> ApplyScalarFilters(
        this IQueryable<UserDatabaseGame> query,
        bool eloEnabled,
        int? eloFrom,
        int? eloTo,
        EloFilterMode eloMode,
        bool yearEnabled,
        int? yearFrom,
        int? yearTo,
        string? ecoCode,
        string? result,
        int? moveCountFrom,
        int? moveCountTo)
    {
        if (eloEnabled && eloFrom.HasValue && eloTo.HasValue)
        {
            var from = eloFrom.Value;
            var to = eloTo.Value;

            query = eloMode switch
            {
                EloFilterMode.One => query.Where(g =>
                    (g.Game.WhiteElo.HasValue && g.Game.WhiteElo.Value >= from && g.Game.WhiteElo.Value <= to) ||
                    (g.Game.BlackElo.HasValue && g.Game.BlackElo.Value >= from && g.Game.BlackElo.Value <= to)),
                EloFilterMode.Both => query.Where(g =>
                    g.Game.WhiteElo.HasValue && g.Game.BlackElo.HasValue &&
                    g.Game.WhiteElo.Value >= from && g.Game.WhiteElo.Value <= to &&
                    g.Game.BlackElo.Value >= from && g.Game.BlackElo.Value <= to),
                EloFilterMode.Avg => query.Where(g =>
                    g.Game.WhiteElo.HasValue && g.Game.BlackElo.HasValue &&
                    ((g.Game.WhiteElo.Value + g.Game.BlackElo.Value) / 2.0) >= from &&
                    ((g.Game.WhiteElo.Value + g.Game.BlackElo.Value) / 2.0) <= to),
                _ => query
            };
        }

        if (yearEnabled && yearFrom.HasValue && yearTo.HasValue)
        {
            var from = yearFrom.Value;
            var to = yearTo.Value;
            query = query.Where(g => g.Game.Year >= from && g.Game.Year <= to);
        }

        if (!string.IsNullOrWhiteSpace(ecoCode))
        {
            var normalizedEcoCode = ecoCode.Trim().ToUpperInvariant();
            query = query.Where(g => g.Game.ECO != null && g.Game.ECO.StartsWith(normalizedEcoCode));
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            var normalizedResult = result.Trim();
            query = query.Where(g => g.Game.Result == normalizedResult);
        }

        if (moveCountFrom.HasValue)
        {
            var from = moveCountFrom.Value;
            query = query.Where(g => g.Game.MoveCount >= from);
        }

        if (moveCountTo.HasValue)
        {
            var to = moveCountTo.Value;
            query = query.Where(g => g.Game.MoveCount <= to);
        }

        return query;
    }

    public static IQueryable<Game> ApplyPlayerFilters(
        this IQueryable<Game> query,
        bool ignoreColors,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName)
    {
        var hasWhite = normalizedWhiteFirstName is not null || normalizedWhiteLastName is not null;
        var hasBlack = normalizedBlackFirstName is not null || normalizedBlackLastName is not null;

        if (!hasWhite && !hasBlack)
        {
            return query;
        }

        if (!ignoreColors)
        {
            if (hasWhite)
            {
                query = query.Where(g =>
                    (normalizedWhiteFirstName == null || g.WhiteNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.WhiteNormalizedLastName == normalizedWhiteLastName));
            }

            if (hasBlack)
            {
                query = query.Where(g =>
                    (normalizedBlackFirstName == null || g.BlackNormalizedFirstName == normalizedBlackFirstName)
                    && (normalizedBlackLastName == null || g.BlackNormalizedLastName == normalizedBlackLastName));
            }

            return query;
        }

        if (hasWhite && hasBlack)
        {
            query = query.Where(g =>
                (
                    (normalizedWhiteFirstName == null || g.WhiteNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.WhiteNormalizedLastName == normalizedWhiteLastName)
                    && (normalizedBlackFirstName == null || g.BlackNormalizedFirstName == normalizedBlackFirstName)
                    && (normalizedBlackLastName == null || g.BlackNormalizedLastName == normalizedBlackLastName)
                )
                ||
                (
                    (normalizedWhiteFirstName == null || g.BlackNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.BlackNormalizedLastName == normalizedWhiteLastName)
                    && (normalizedBlackFirstName == null || g.WhiteNormalizedFirstName == normalizedBlackFirstName)
                    && (normalizedBlackLastName == null || g.WhiteNormalizedLastName == normalizedBlackLastName)
                ));
            return query;
        }

        if (hasWhite)
        {
            return query.Where(g =>
                (
                    (normalizedWhiteFirstName == null || g.WhiteNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.WhiteNormalizedLastName == normalizedWhiteLastName)
                )
                ||
                (
                    (normalizedWhiteFirstName == null || g.BlackNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.BlackNormalizedLastName == normalizedWhiteLastName)
                ));
        }

        return query.Where(g =>
            (
                (normalizedBlackFirstName == null || g.WhiteNormalizedFirstName == normalizedBlackFirstName)
                && (normalizedBlackLastName == null || g.WhiteNormalizedLastName == normalizedBlackLastName)
            )
            ||
            (
                (normalizedBlackFirstName == null || g.BlackNormalizedFirstName == normalizedBlackFirstName)
                && (normalizedBlackLastName == null || g.BlackNormalizedLastName == normalizedBlackLastName)
            ));
    }

    public static IQueryable<StagingGame> ApplyPlayerFilters(
        this IQueryable<StagingGame> query,
        bool ignoreColors,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName)
    {
        var hasWhite = normalizedWhiteFirstName is not null || normalizedWhiteLastName is not null;
        var hasBlack = normalizedBlackFirstName is not null || normalizedBlackLastName is not null;

        if (!hasWhite && !hasBlack)
        {
            return query;
        }

        if (!ignoreColors)
        {
            if (hasWhite)
            {
                query = query.Where(g =>
                    (normalizedWhiteFirstName == null || g.WhiteNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.WhiteNormalizedLastName == normalizedWhiteLastName));
            }

            if (hasBlack)
            {
                query = query.Where(g =>
                    (normalizedBlackFirstName == null || g.BlackNormalizedFirstName == normalizedBlackFirstName)
                    && (normalizedBlackLastName == null || g.BlackNormalizedLastName == normalizedBlackLastName));
            }

            return query;
        }

        if (hasWhite && hasBlack)
        {
            query = query.Where(g =>
                (
                    (normalizedWhiteFirstName == null || g.WhiteNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.WhiteNormalizedLastName == normalizedWhiteLastName)
                    && (normalizedBlackFirstName == null || g.BlackNormalizedFirstName == normalizedBlackFirstName)
                    && (normalizedBlackLastName == null || g.BlackNormalizedLastName == normalizedBlackLastName)
                )
                ||
                (
                    (normalizedWhiteFirstName == null || g.BlackNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.BlackNormalizedLastName == normalizedWhiteLastName)
                    && (normalizedBlackFirstName == null || g.WhiteNormalizedFirstName == normalizedBlackFirstName)
                    && (normalizedBlackLastName == null || g.WhiteNormalizedLastName == normalizedBlackLastName)
                ));
            return query;
        }

        if (hasWhite)
        {
            return query.Where(g =>
                (
                    (normalizedWhiteFirstName == null || g.WhiteNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.WhiteNormalizedLastName == normalizedWhiteLastName)
                )
                ||
                (
                    (normalizedWhiteFirstName == null || g.BlackNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.BlackNormalizedLastName == normalizedWhiteLastName)
                ));
        }

        return query.Where(g =>
            (
                (normalizedBlackFirstName == null || g.WhiteNormalizedFirstName == normalizedBlackFirstName)
                && (normalizedBlackLastName == null || g.WhiteNormalizedLastName == normalizedBlackLastName)
            )
            ||
            (
                (normalizedBlackFirstName == null || g.BlackNormalizedFirstName == normalizedBlackFirstName)
                && (normalizedBlackLastName == null || g.BlackNormalizedLastName == normalizedBlackLastName)
            ));
    }

    public static IQueryable<UserDatabaseGame> ApplyPlayerFilters(
        this IQueryable<UserDatabaseGame> query,
        bool ignoreColors,
        string? normalizedWhiteFirstName,
        string? normalizedWhiteLastName,
        string? normalizedBlackFirstName,
        string? normalizedBlackLastName)
    {
        var hasWhite = normalizedWhiteFirstName is not null || normalizedWhiteLastName is not null;
        var hasBlack = normalizedBlackFirstName is not null || normalizedBlackLastName is not null;

        if (!hasWhite && !hasBlack)
        {
            return query;
        }

        if (!ignoreColors)
        {
            if (hasWhite)
            {
                query = query.Where(g =>
                    (normalizedWhiteFirstName == null || g.Game.WhiteNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.Game.WhiteNormalizedLastName == normalizedWhiteLastName));
            }

            if (hasBlack)
            {
                query = query.Where(g =>
                    (normalizedBlackFirstName == null || g.Game.BlackNormalizedFirstName == normalizedBlackFirstName)
                    && (normalizedBlackLastName == null || g.Game.BlackNormalizedLastName == normalizedBlackLastName));
            }

            return query;
        }

        if (hasWhite && hasBlack)
        {
            query = query.Where(g =>
                (
                    (normalizedWhiteFirstName == null || g.Game.WhiteNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.Game.WhiteNormalizedLastName == normalizedWhiteLastName)
                    && (normalizedBlackFirstName == null || g.Game.BlackNormalizedFirstName == normalizedBlackFirstName)
                    && (normalizedBlackLastName == null || g.Game.BlackNormalizedLastName == normalizedBlackLastName)
                )
                ||
                (
                    (normalizedWhiteFirstName == null || g.Game.BlackNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.Game.BlackNormalizedLastName == normalizedWhiteLastName)
                    && (normalizedBlackFirstName == null || g.Game.WhiteNormalizedFirstName == normalizedBlackFirstName)
                    && (normalizedBlackLastName == null || g.Game.WhiteNormalizedLastName == normalizedBlackLastName)
                ));
            return query;
        }

        if (hasWhite)
        {
            return query.Where(g =>
                (
                    (normalizedWhiteFirstName == null || g.Game.WhiteNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.Game.WhiteNormalizedLastName == normalizedWhiteLastName)
                )
                ||
                (
                    (normalizedWhiteFirstName == null || g.Game.BlackNormalizedFirstName == normalizedWhiteFirstName)
                    && (normalizedWhiteLastName == null || g.Game.BlackNormalizedLastName == normalizedWhiteLastName)
                ));
        }

        return query.Where(g =>
            (
                (normalizedBlackFirstName == null || g.Game.WhiteNormalizedFirstName == normalizedBlackFirstName)
                && (normalizedBlackLastName == null || g.Game.WhiteNormalizedLastName == normalizedBlackLastName)
            )
            ||
            (
                (normalizedBlackFirstName == null || g.Game.BlackNormalizedFirstName == normalizedBlackFirstName)
                && (normalizedBlackLastName == null || g.Game.BlackNormalizedLastName == normalizedBlackLastName)
            ));
    }

    public static IQueryable<Game> ApplyPositionFilters(
        this IQueryable<Game> query,
        bool searchByPosition,
        string? normalizedFen,
        long? fenHash,
        PositionSearchMode positionMode)
    {
        if (!searchByPosition)
        {
            return query;
        }

        if (positionMode == PositionSearchMode.Exact)
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

        if (positionMode == PositionSearchMode.SamePosition)
        {
            if (!fenHash.HasValue)
            {
                return query;
            }

            var hash = fenHash.Value;
            return query.Where(g => g.Positions.Any(p => p.FenHash == hash));
        }

        return query;
    }

    public static IQueryable<StagingGame> ApplyPositionFilters(
        this IQueryable<StagingGame> query,
        bool searchByPosition,
        string? normalizedFen,
        long? fenHash,
        PositionSearchMode positionMode)
    {
        if (!searchByPosition || string.IsNullOrWhiteSpace(normalizedFen))
        {
            return query;
        }

        if (positionMode == PositionSearchMode.Exact)
        {
            return query.Where(g => g.Positions.Any(p => p.Fen == normalizedFen));
        }

        if (positionMode == PositionSearchMode.SamePosition)
        {
            if (!fenHash.HasValue)
            {
                return query;
            }

            var hash = fenHash.Value;
            return query.Where(g => g.Positions.Any(p => p.FenHash == hash));
        }

        return query;
    }

    public static IQueryable<UserDatabaseGame> ApplyPositionFilters(
        this IQueryable<UserDatabaseGame> query,
        bool searchByPosition,
        string? normalizedFen,
        long? fenHash,
        PositionSearchMode positionMode)
    {
        if (!searchByPosition || string.IsNullOrWhiteSpace(normalizedFen))
        {
            return query;
        }

        if (positionMode == PositionSearchMode.Exact)
        {
            return query.Where(g => g.Game.Positions.Any(p => p.Fen == normalizedFen));
        }

        if (positionMode == PositionSearchMode.SamePosition)
        {
            if (!fenHash.HasValue)
            {
                return query;
            }

            var hash = fenHash.Value;
            return query.Where(g => g.Game.Positions.Any(p => p.FenHash == hash));
        }

        return query;
    }
}