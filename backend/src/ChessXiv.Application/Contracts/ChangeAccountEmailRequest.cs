namespace ChessXiv.Application.Contracts;

public sealed record ChangeAccountEmailRequest(string NewEmail, string CurrentPassword);
