namespace ChessXiv.Application.Contracts;

public sealed record UpdateUserDatabaseRequest(string Name, bool IsPublic);
