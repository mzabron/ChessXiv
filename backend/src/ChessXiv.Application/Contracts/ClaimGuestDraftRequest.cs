namespace ChessXiv.Application.Contracts;

public sealed record ClaimGuestDraftRequest(string GuestToken);

public sealed record ClaimGuestDraftResponse(bool Claimed, int GameCount);
