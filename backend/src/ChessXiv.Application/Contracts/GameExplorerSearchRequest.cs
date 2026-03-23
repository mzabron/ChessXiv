namespace ChessXiv.Application.Contracts;

public class GameExplorerSearchRequest
{
    public Guid? UserDatabaseId { get; set; }

    public string? WhiteFirstName { get; set; }
    public string? WhiteLastName { get; set; }
    public string? BlackFirstName { get; set; }
    public string? BlackLastName { get; set; }
    public bool IgnoreColors { get; set; }

    public bool EloEnabled { get; set; }
    public int? EloFrom { get; set; }
    public int? EloTo { get; set; }
    public EloFilterMode EloMode { get; set; } = EloFilterMode.None;

    public bool YearEnabled { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }

    public string? EcoCode { get; set; }
    public string? Result { get; set; }

    public int? MoveCountFrom { get; set; }
    public int? MoveCountTo { get; set; }

    public bool SearchByPosition { get; set; }
    public string? Fen { get; set; }
    public PositionSearchMode PositionMode { get; set; } = PositionSearchMode.Exact;

    public GameExplorerSortBy SortBy { get; set; } = GameExplorerSortBy.Year;
    public SortDirection SortDirection { get; set; } = SortDirection.Desc;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
