namespace ChessXiv.Application.Contracts;

/// <summary>
/// Reported after each committed batch of a direct database import. A CLI import of a
/// multi-gigabyte PGN runs for a long time, and without this it prints nothing at all
/// between "Importing games from ..." and the final summary.
/// </summary>
public sealed record ImportProgress(
    int ParsedCount,
    int ImportedCount,
    int SkippedCount);
