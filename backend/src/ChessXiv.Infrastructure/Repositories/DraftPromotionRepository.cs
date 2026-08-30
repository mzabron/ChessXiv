using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Infrastructure.Repositories;

public sealed class DraftPromotionRepository(ChessXivDbContext dbContext) : IDraftPromotionRepository
{
    public Task<UserDatabase?> GetUserDatabaseAsync(Guid userDatabaseId, CancellationToken cancellationToken = default)
    {
        return dbContext.UserDatabases
            .FirstOrDefaultAsync(d => d.Id == userDatabaseId, cancellationToken);
    }

    public Task<int> CountSavedGamesAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        return dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabase.OwnerUserId == ownerUserId)
            .Select(link => link.GameId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public Task<int> CountStagingGamesAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        return dbContext.StagingGames
            .AsNoTracking()
            .CountAsync(g => g.OwnerUserId == ownerUserId, cancellationToken);
    }

    public Task SyncGameCountAsync(Guid userDatabaseId, CancellationToken cancellationToken = default)
    {
        return dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "UserDatabases" d
            SET "GameCount" = (
                SELECT count(*) FROM "UserDatabaseGames" l WHERE l."UserDatabaseId" = d."Id"
            ),
            "ContentUpdatedAtUtc" = now() AT TIME ZONE 'utc'
            WHERE d."Id" = {userDatabaseId};
            """, cancellationToken);
    }

    public async Task<int> PromoteSelectionAsync(
        string ownerUserId,
        Guid userDatabaseId,
        IReadOnlyCollection<Guid> stagingGameIds,
        DateTime addedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (stagingGameIds.Count == 0)
        {
            return 0;
        }

        var ids = stagingGameIds as Guid[] ?? stagingGameIds.ToArray();
        var previousTimeoutSeconds = dbContext.Database.GetCommandTimeout();
        dbContext.Database.SetCommandTimeout((int)TimeSpan.FromMinutes(5).TotalSeconds);

        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Games" (
                    "Id", "Date", "Year", "Round", "WhiteTitle", "BlackTitle", "WhiteElo", "BlackElo",
                    "Event", "Site", "TimeControl", "ECO", "Opening", "White", "Black",
                    "WhiteNormalizedFullName", "WhiteNormalizedFirstName", "WhiteNormalizedLastName",
                    "BlackNormalizedFullName", "BlackNormalizedFirstName", "BlackNormalizedLastName",
                    "Result", "Pgn", "MoveCount", "GameHash"
                )
                SELECT
                    sg."Id", sg."Date", sg."Year", sg."Round", sg."WhiteTitle", sg."BlackTitle", sg."WhiteElo", sg."BlackElo",
                    sg."Event", sg."Site", sg."TimeControl", sg."ECO", sg."Opening", sg."White", sg."Black",
                    sg."WhiteNormalizedFullName", sg."WhiteNormalizedFirstName", sg."WhiteNormalizedLastName",
                    sg."BlackNormalizedFullName", sg."BlackNormalizedFirstName", sg."BlackNormalizedLastName",
                    sg."Result", sg."Pgn", sg."MoveCount", sg."GameHash"
                FROM "StagingGames" sg
                WHERE sg."OwnerUserId" = {ownerUserId} AND sg."Id" = ANY({ids})
                ON CONFLICT ("Id") DO NOTHING;

                INSERT INTO "Positions" (
                    "GameId", "PlyCount", "PosKey", "NextMove", "Result"
                )
                SELECT
                    sp."StagingGameId", sp."PlyCount", sp."PosKey", sp."NextMove", sp."Result"
                FROM "StagingPositions" sp
                INNER JOIN "StagingGames" sg ON sg."Id" = sp."StagingGameId"
                WHERE sg."OwnerUserId" = {ownerUserId} AND sg."Id" = ANY({ids})
                ON CONFLICT ("GameId", "PlyCount") DO NOTHING;
                """, cancellationToken);

            return await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "UserDatabaseGames" (
                    "UserDatabaseId", "GameId", "AddedAtUtc", "Date", "Year", "Event", "Round", "Site"
                )
                SELECT
                    {userDatabaseId}, sg."Id", {addedAtUtc}, sg."Date",
                    CASE WHEN sg."Year" <= 0 THEN NULL ELSE sg."Year" END,
                    sg."Event", sg."Round", sg."Site"
                FROM "StagingGames" sg
                WHERE sg."OwnerUserId" = {ownerUserId} AND sg."Id" = ANY({ids})
                ON CONFLICT ("UserDatabaseId", "GameId") DO NOTHING;
                """, cancellationToken);
        }
        finally
        {
            dbContext.Database.SetCommandTimeout(previousTimeoutSeconds);
        }
    }

    public async Task<int> PromoteAllAsync(
        string ownerUserId,
        Guid userDatabaseId,
        DateTime addedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var previousTimeoutSeconds = dbContext.Database.GetCommandTimeout();
        dbContext.Database.SetCommandTimeout((int)TimeSpan.FromMinutes(5).TotalSeconds);

        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Games" (
                    "Id", "Date", "Year", "Round", "WhiteTitle", "BlackTitle", "WhiteElo", "BlackElo",
                    "Event", "Site", "TimeControl", "ECO", "Opening", "White", "Black",
                    "WhiteNormalizedFullName", "WhiteNormalizedFirstName", "WhiteNormalizedLastName",
                    "BlackNormalizedFullName", "BlackNormalizedFirstName", "BlackNormalizedLastName",
                    "Result", "Pgn", "MoveCount", "GameHash"
                )
                SELECT
                    sg."Id", sg."Date", sg."Year", sg."Round", sg."WhiteTitle", sg."BlackTitle", sg."WhiteElo", sg."BlackElo",
                    sg."Event", sg."Site", sg."TimeControl", sg."ECO", sg."Opening", sg."White", sg."Black",
                    sg."WhiteNormalizedFullName", sg."WhiteNormalizedFirstName", sg."WhiteNormalizedLastName",
                    sg."BlackNormalizedFullName", sg."BlackNormalizedFirstName", sg."BlackNormalizedLastName",
                    sg."Result", sg."Pgn", sg."MoveCount", sg."GameHash"
                FROM "StagingGames" sg
                WHERE sg."OwnerUserId" = {ownerUserId}
                ON CONFLICT ("Id") DO NOTHING;

                INSERT INTO "Positions" (
                    "GameId", "PlyCount", "PosKey", "NextMove", "Result"
                )
                SELECT
                    sp."StagingGameId", sp."PlyCount", sp."PosKey", sp."NextMove", sp."Result"
                FROM "StagingPositions" sp
                INNER JOIN "StagingGames" sg ON sg."Id" = sp."StagingGameId"
                WHERE sg."OwnerUserId" = {ownerUserId}
                ON CONFLICT ("GameId", "PlyCount") DO NOTHING;

                INSERT INTO "UserDatabaseGames" (
                    "UserDatabaseId", "GameId", "AddedAtUtc", "Date", "Year", "Event", "Round", "Site"
                )
                SELECT
                    {userDatabaseId}, sg."Id", {addedAtUtc}, sg."Date",
                    CASE WHEN sg."Year" <= 0 THEN NULL ELSE sg."Year" END,
                    sg."Event", sg."Round", sg."Site"
                FROM "StagingGames" sg
                WHERE sg."OwnerUserId" = {ownerUserId}
                ON CONFLICT ("UserDatabaseId", "GameId") DO NOTHING;
                """, cancellationToken);

            var promotedCount = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "StagingGames"
                WHERE "OwnerUserId" = {ownerUserId};
                """, cancellationToken);

            return promotedCount;
        }
        finally
        {
            dbContext.Database.SetCommandTimeout(previousTimeoutSeconds);
        }
    }

}
