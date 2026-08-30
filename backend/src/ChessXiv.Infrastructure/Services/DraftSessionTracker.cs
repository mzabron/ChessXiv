using ChessXiv.Application.Abstractions;
using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Infrastructure.Services;

public sealed class DraftSessionTracker(ChessXivDbContext dbContext) : IDraftSessionTracker
{
    public Task TouchAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return Task.CompletedTask;
        }

        // Single-statement upsert: touching the session must never cost more than one
        // round trip, because it happens on every draft page load.
        return dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "StagingDraftSessions" ("OwnerUserId", "CreatedAtUtc", "LastAccessedAtUtc")
            VALUES ({ownerUserId}, {DateTime.UtcNow}, {DateTime.UtcNow})
            ON CONFLICT ("OwnerUserId")
            DO UPDATE SET "LastAccessedAtUtc" = EXCLUDED."LastAccessedAtUtc";
            """, cancellationToken);
    }

    public Task ClearAsync(string ownerUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return Task.CompletedTask;
        }

        return dbContext.StagingDraftSessions
            .Where(s => s.OwnerUserId == ownerUserId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
