namespace ChessXiv.Application.Contracts;

public sealed record DraftGamesPageResponse(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyCollection<DraftGameListItem> Items);