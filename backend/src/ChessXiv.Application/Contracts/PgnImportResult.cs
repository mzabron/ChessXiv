namespace ChessXiv.Application.Contracts;

public sealed record PgnImportResult(
    int ParsedCount,
    int ImportedCount,
    int SkippedCount
);
