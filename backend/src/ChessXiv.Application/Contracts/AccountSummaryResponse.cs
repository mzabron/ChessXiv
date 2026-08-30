namespace ChessXiv.Application.Contracts;

public sealed record AccountSummaryResponse(
    string Nickname,
    string Email,
    int SavedGamesUsed,
    int SavedGamesLimit,
    long MaxUploadBytes);
