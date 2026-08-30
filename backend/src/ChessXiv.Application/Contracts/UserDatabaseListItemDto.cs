namespace ChessXiv.Application.Contracts;

/// <summary>
/// One row of the databases panel. The same shape is returned to anonymous and
/// authenticated callers so that signing in never changes which databases are visible.
/// </summary>
public sealed record UserDatabaseListItemDto(
    Guid Id,
    string Name,
    bool IsPublic,
    string OwnerUserId,
    string OwnerUserName,
    int GameCount,
    DateTime CreatedAtUtc,
    DateTime ContentUpdatedAtUtc,
    bool IsOwner,
    bool IsBookmarked);
