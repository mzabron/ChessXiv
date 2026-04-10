using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions;

public interface IPgnImportService
{
    Task<PgnImportResult> ImportAsync(TextReader reader, bool markAsMaster = false, int batchSize = 500, CancellationToken cancellationToken = default);
}
