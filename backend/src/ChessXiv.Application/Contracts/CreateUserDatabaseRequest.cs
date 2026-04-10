namespace ChessXiv.Application.Contracts;

public sealed record CreateUserDatabaseRequest(string Name, bool IsPublic);
