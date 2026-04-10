namespace ChessXiv.Application.Contracts;

public sealed record AuthRegisterResponse(bool RequiresEmailConfirmation, string Email, string Message);