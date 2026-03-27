using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions;

public interface IDraftImportProgressPublisher
{
    Task PublishAsync(string ownerUserId, DraftImportProgressUpdate update, CancellationToken cancellationToken = default);
}