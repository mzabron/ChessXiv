using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Api.Services;

public sealed class StagingDraftCleanupService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<StagingDraftCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a draft may sit untouched before it is swept. The clock is reset by every
    /// read of the draft, so a draft is only ever removed once nobody is looking at it -
    /// closing the tab drops the guest's session token, which makes the draft unreachable at
    /// once (registered users additionally get an explicit, immediate delete via "Close").
    /// 24h matches what a signed-in user would reasonably expect an abandoned import to
    /// survive across, without keeping truly forgotten drafts around indefinitely.
    /// </summary>
    private static readonly TimeSpan MaxIdleTime = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        await CleanupAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ChessXivDbContext>();
            var threshold = DateTime.UtcNow.Subtract(MaxIdleTime);

            var idleOwners = await dbContext.StagingDraftSessions
                .AsNoTracking()
                .Where(session => session.LastAccessedAtUtc <= threshold)
                .Select(session => session.OwnerUserId)
                .ToListAsync(cancellationToken);

            // Drafts whose session row never existed (or was lost) are swept on creation age
            // so that no staging data can outlive the sweep window unattended.
            var orphanOwners = await dbContext.StagingGames
                .AsNoTracking()
                .Where(g => g.CreatedAtUtc <= threshold
                            && !dbContext.StagingDraftSessions.Any(s => s.OwnerUserId == g.OwnerUserId))
                .Select(g => g.OwnerUserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var staleOwners = idleOwners.Concat(orphanOwners).Distinct().ToList();
            if (staleOwners.Count == 0)
            {
                return;
            }

            var deletedCount = await dbContext.StagingGames
                .Where(g => staleOwners.Contains(g.OwnerUserId))
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.StagingDraftSessions
                .Where(s => staleOwners.Contains(s.OwnerUserId))
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedCount > 0)
            {
                logger.LogInformation(
                    "Deleted {Count} staging games for {Owners} drafts idle for more than {Hours}h.",
                    deletedCount,
                    staleOwners.Count,
                    MaxIdleTime.TotalHours);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception while cleaning stale staging games.");
        }
    }
}