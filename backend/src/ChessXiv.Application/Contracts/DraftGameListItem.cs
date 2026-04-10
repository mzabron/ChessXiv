namespace ChessXiv.Application.Contracts;

public sealed record DraftGameListItem(
    Guid Id,
    int Year,
    string White,
    int? WhiteElo,
    string Result,
    string Black,
    int? BlackElo,
    string? Eco,
    string? Event,
    int MoveCount,
    DateTime CreatedAtUtc);