namespace ChessXiv.Application.Contracts;

public sealed record AddGamesToDatabaseRequest(IReadOnlyCollection<Guid> GameIds);
