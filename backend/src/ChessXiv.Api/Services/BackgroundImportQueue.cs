using System.Threading.Channels;

namespace ChessXiv.Api.Services;

public enum ImportTargetType
{
    Draft,
    UserDatabase
}

public class BackgroundImportJob
{
    public required string UserId { get; init; }
    public required string TempFilePath { get; init; }
    public required ImportTargetType TargetType { get; init; }
    public Guid? UserDatabaseId { get; init; }
}

public class BackgroundImportQueue
{
    private readonly Channel<BackgroundImportJob> _queue;

    public BackgroundImportQueue()
    {
        var options = new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<BackgroundImportJob>(options);
    }

    public async ValueTask QueueBackgroundWorkItemAsync(BackgroundImportJob workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        await _queue.Writer.WriteAsync(workItem);
    }

    public async ValueTask<BackgroundImportJob> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}
