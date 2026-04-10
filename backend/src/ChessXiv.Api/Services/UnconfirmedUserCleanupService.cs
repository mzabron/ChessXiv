using ChessXiv.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Api.Services;

public sealed class UnconfirmedUserCleanupService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<UnconfirmedUserCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxUnconfirmedAge = TimeSpan.FromHours(24);

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
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var threshold = DateTime.UtcNow.Subtract(MaxUnconfirmedAge);

            var staleUsers = await userManager.Users
                .Where(u => !u.EmailConfirmed && u.CreatedAtUtc <= threshold)
                .ToListAsync(cancellationToken);

            if (staleUsers.Count == 0)
            {
                return;
            }

            var deletedCount = 0;
            foreach (var user in staleUsers)
            {
                var result = await userManager.DeleteAsync(user);
                if (result.Succeeded)
                {
                    deletedCount++;
                    continue;
                }

                logger.LogWarning(
                    "Failed to delete stale unconfirmed user {UserId}. Errors: {Errors}",
                    user.Id,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }

            if (deletedCount > 0)
            {
                logger.LogInformation("Deleted {Count} unconfirmed users older than {Hours} hours.", deletedCount, MaxUnconfirmedAge.TotalHours);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception while cleaning stale unconfirmed users.");
        }
    }
}
