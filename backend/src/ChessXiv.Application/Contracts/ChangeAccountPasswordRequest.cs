namespace ChessXiv.Application.Contracts;

public sealed record ChangeAccountPasswordRequest(string CurrentPassword, string NewPassword);
