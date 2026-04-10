namespace ChessXiv.Application.Contracts;

public sealed record DraftImportResult(
    int ParsedCount,
    int ImportedCount,
    int SkippedCount);
