namespace ChessXiv.Application.Contracts;

public sealed record AuthRegisterRequest(string Login, string Email, string Password);
