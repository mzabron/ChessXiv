namespace ChessXiv.Application.Contracts;

public sealed record BookmarkedUserDatabaseDto(
    Guid Id,
    string Name,
    bool IsPublic,
    string OwnerUserId,
    int GameCount,
    DateTime CreatedAtUtc,
    DateTime BookmarkedAtUtc);
