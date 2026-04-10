namespace ChessXiv.Application.Contracts;

public sealed record DirectImportToDatabaseRequest(string Pgn, Guid UserDatabaseId);
