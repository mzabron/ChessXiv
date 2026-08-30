namespace ChessXiv.Application.Contracts;

public sealed record RemoveGamesFromDatabaseRequest(IReadOnlyCollection<Guid> GameIds);

public sealed record RemoveGamesFromDatabaseResponse(int RemovedCount, int DeletedOrphanCount);
