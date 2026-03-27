using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Api.Services;

public sealed class StagingDraftCleanupService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<StagingDraftCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxDraftAge = TimeSpan.FromHours(24);

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
            var threshold = DateTime.UtcNow.Subtract(MaxDraftAge);

            var deletedCount = await dbContext.StagingGames
                .Where(g => g.CreatedAtUtc <= threshold)
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedCount > 0)
            {
                logger.LogInformation("Deleted {Count} stale staging games older than {Hours} hours.", deletedCount, MaxDraftAge.TotalHours);
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