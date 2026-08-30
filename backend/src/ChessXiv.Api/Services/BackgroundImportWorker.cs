using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Services;

namespace ChessXiv.Api.Services;

public class BackgroundImportWorker(
    BackgroundImportQueue taskQueue,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<BackgroundImportWorker> logger) : BackgroundService
{
    /// <summary>
    /// How many imports may run at once. Each one streams a PGN of up to 100 MB and holds a
    /// batch of parsed games plus their positions in memory, so unbounded fan-out could
    /// exhaust RAM on a small host. Two keeps a second user from waiting behind a long
    /// import without putting the box at risk.
    /// </summary>
    private const int MaxConcurrentImports = 2;

    private readonly SemaphoreSlim _concurrencyLimiter = new(MaxConcurrentImports, MaxConcurrentImports);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background Import Worker is starting.");

        var running = new List<Task>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await taskQueue.DequeueAsync(stoppingToken);

                // Waiting here rather than inside the task keeps queued work queued instead
                // of piling up as thousands of started-but-blocked tasks.
                await _concurrencyLimiter.WaitAsync(stoppingToken);

                running.RemoveAll(task => task.IsCompleted);
                running.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessWorkItemAsync(workItem, stoppingToken);
                    }
                    finally
                    {
                        _concurrencyLimiter.Release();
                    }
                }, stoppingToken));
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

        // Let in-flight imports finish so a shutdown does not leave a half-written draft.
        await Task.WhenAll(running.Where(task => !task.IsCompleted));

        logger.LogInformation("Background Import Worker is stopping.");
    }

    public override void Dispose()
    {
        _concurrencyLimiter.Dispose();
        base.Dispose();
    }

    private async Task ProcessWorkItemAsync(BackgroundImportJob workItem, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            
            using var fileStream = File.OpenRead(workItem.TempFilePath);
            // Uploaded PGNs are frequently not UTF-8; reading them as UTF-8 regardless
            // turned every accented player name into replacement characters.
            using var reader = PgnEncoding.OpenReader(fileStream, forced: null, out var encoding);
            logger.LogInformation(
                "Reading uploaded PGN for {UserId} as {Encoding}.", workItem.UserId, encoding.WebName);

            if (workItem.TargetType == ImportTargetType.Draft)
            {
                var draftImportService = scope.ServiceProvider.GetRequiredService<IDraftImportService>();
                var result = await draftImportService.ImportAsync(
                    reader,
                    workItem.UserId,
                    batchSize: 200,
                    cancellationToken: stoppingToken);
                    
                await scope.ServiceProvider
                    .GetRequiredService<IImportStatisticsRefresher>()
                    .RefreshAfterDraftImportAsync(stoppingToken);

                var progressPublisher = scope.ServiceProvider.GetService<ChessXiv.Application.Abstractions.IDraftImportProgressPublisher>();
                if (progressPublisher is not null)
                {
                    var update = new ChessXiv.Application.Contracts.DraftImportProgressUpdate(
                        result.ParsedCount,
                        result.ImportedCount,
                        result.SkippedCount,
                        IsCompleted: true,
                        IsFailed: false,
                        Message: "Import completed.");
                    await progressPublisher.PublishAsync(workItem.UserId, update, stoppingToken);
                }
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

                await scope.ServiceProvider
                    .GetRequiredService<IImportStatisticsRefresher>()
                    .RefreshAfterDatabaseImportAsync(stoppingToken);
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
