namespace ChessXiv.Application.Contracts;

public sealed record UserDatabaseDto(
    Guid Id,
    string Name,
    bool IsPublic,
    string OwnerUserId,
    string OwnerUserName,
    int GameCount,
    DateTime CreatedAtUtc);
