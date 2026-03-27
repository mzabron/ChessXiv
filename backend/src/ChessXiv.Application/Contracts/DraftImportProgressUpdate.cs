namespace ChessXiv.Application.Contracts;

public sealed record DraftImportProgressUpdate(
    int ParsedCount,
    int ImportedCount,
    int SkippedCount,
    bool IsCompleted,
    bool IsFailed,
    string? Message = null);