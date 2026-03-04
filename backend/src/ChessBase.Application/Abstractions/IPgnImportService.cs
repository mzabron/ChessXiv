using ChessBase.Application.Contracts;

namespace ChessBase.Application.Abstractions;

public interface IPgnImportService
{
    Task<PgnImportResult> ImportAsync(string pgn, CancellationToken cancellationToken = default);
}
