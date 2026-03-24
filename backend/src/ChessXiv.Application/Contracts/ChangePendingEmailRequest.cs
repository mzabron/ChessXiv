namespace ChessXiv.Application.Contracts;

public sealed record ChangePendingEmailRequest(string UsernameOrEmail, string Password, string NewEmail);
