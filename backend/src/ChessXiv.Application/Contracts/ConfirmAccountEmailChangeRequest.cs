namespace ChessXiv.Application.Contracts;

public sealed record ConfirmAccountEmailChangeRequest(string UserId, string NewEmail, string Token);
