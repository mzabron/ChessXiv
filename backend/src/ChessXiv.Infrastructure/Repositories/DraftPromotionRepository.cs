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
                    "Result", "Pgn", "MoveCount", "GameHash", "IsMaster"
                )
                SELECT
                    sg."Id", sg."Date", sg."Year", sg."Round", sg."WhiteTitle", sg."BlackTitle", sg."WhiteElo", sg."BlackElo",
                    sg."Event", sg."Site", sg."TimeControl", sg."ECO", sg."Opening", sg."White", sg."Black",
                    sg."WhiteNormalizedFullName", sg."WhiteNormalizedFirstName", sg."WhiteNormalizedLastName",
                    sg."BlackNormalizedFullName", sg."BlackNormalizedFirstName", sg."BlackNormalizedLastName",
                    sg."Result", sg."Pgn", sg."MoveCount", sg."GameHash", FALSE
                FROM "StagingGames" sg
                WHERE sg."OwnerUserId" = {ownerUserId}
                ON CONFLICT ("Id") DO NOTHING;

                INSERT INTO "Moves" (
                    "Id", "GameId", "MoveNumber", "WhiteMove", "BlackMove", "WhiteClk", "BlackClk", "WhiteEval", "BlackEval"
                )
                SELECT
                    sm."Id", sm."StagingGameId", sm."MoveNumber", sm."WhiteMove", sm."BlackMove", sm."WhiteClk", sm."BlackClk", sm."WhiteEval", sm."BlackEval"
                FROM "StagingMoves" sm
                INNER JOIN "StagingGames" sg ON sg."Id" = sm."StagingGameId"
                WHERE sg."OwnerUserId" = {ownerUserId}
                ON CONFLICT ("Id") DO NOTHING;

                INSERT INTO "Positions" (
                    "Id", "GameId", "Fen", "FenHash", "PlyCount", "LastMove", "SideToMove"
                )
                SELECT
                    sp."Id", sp."StagingGameId", sp."Fen", sp."FenHash", sp."PlyCount", sp."LastMove", sp."SideToMove"
                FROM "StagingPositions" sp
                INNER JOIN "StagingGames" sg ON sg."Id" = sp."StagingGameId"
                WHERE sg."OwnerUserId" = {ownerUserId}
                ON CONFLICT ("Id") DO NOTHING;

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
