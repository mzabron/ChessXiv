namespace ChessXiv.Application.Contracts;

public sealed record AuthTokenResponse(string AccessToken, DateTime ExpiresAtUtc);
