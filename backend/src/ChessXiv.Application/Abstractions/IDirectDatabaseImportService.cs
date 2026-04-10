using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions;

public interface IDirectDatabaseImportService
{
    Task<DraftImportResult> ImportToDatabaseAsync(
        TextReader reader,
        string ownerUserId,
        Guid userDatabaseId,
        int batchSize = 500,
        CancellationToken cancellationToken = default);
}
