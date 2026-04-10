namespace ChessXiv.Application.Contracts;

public sealed record DraftPromotionResult(
    int PromotedCount,
    int SkippedCount);
