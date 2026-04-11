using ChessXiv.Application.Abstractions;

namespace ChessXiv.Api.Services;

public class BackgroundImportWorker(
    BackgroundImportQueue taskQueue,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<BackgroundImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background Import Worker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await taskQueue.DequeueAsync(stoppingToken);

                _ = Task.Run(() => ProcessWorkItemAsync(workItem, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Prevent throwing if stoppingToken was signaled
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred fetching work item.");
            }
        }

        logger.LogInformation("Background Import Worker is stopping.");
    }

    private async Task ProcessWorkItemAsync(BackgroundImportJob workItem, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            
            using var fileStream = File.OpenRead(workItem.TempFilePath);
            using var reader = new StreamReader(fileStream);

            if (workItem.TargetType == ImportTargetType.Draft)
            {
                var draftImportService = scope.ServiceProvider.GetRequiredService<IDraftImportService>();
                await draftImportService.ImportAsync(
                    reader,
                    workItem.UserId,
                    batchSize: 200,
                    cancellationToken: stoppingToken);
            }
            else if (workItem.TargetType == ImportTargetType.UserDatabase && workItem.UserDatabaseId.HasValue)
            {
                var directDatabaseImportService = scope.ServiceProvider.GetRequiredService<IDirectDatabaseImportService>();
                await directDatabaseImportService.ImportToDatabaseAsync(
                    reader,
                    workItem.UserId,
                    workItem.UserDatabaseId.Value,
                    batchSize: 500,
                    cancellationToken: stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Import job for {UserId} was cancelled.", workItem.UserId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred executing import for user {UserId}", workItem.UserId);
            
            // Note: Since DraftImportService and DirectDatabaseImportService handle
            // capturing and publishing errors to the SignalR Hub, we simply log here.
        }
        finally
        {
            try
            {
                if (File.Exists(workItem.TempFilePath))
                {
                    File.Delete(workItem.TempFilePath);
                }
            }
            catch (Exception cleanupEx)
            {
                logger.LogError(cleanupEx, "Could not delete temporary file {TempFilePath}", workItem.TempFilePath);
            }
        }
    }
}
