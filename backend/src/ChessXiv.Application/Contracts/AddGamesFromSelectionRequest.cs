namespace ChessXiv.Application.Contracts;

/// <summary>
/// Adds games to a database either from the caller's draft or from another database they
/// can read. <see cref="GameIds"/> takes precedence when present (an explicit tick-box
/// selection); otherwise the whole filtered result set is added, which is the common case
/// and may be far larger than the visible page.
/// </summary>
public sealed record AddGamesFromSelectionRequest
{
    /// <summary>Source database, or null to take games from the caller's draft.</summary>
    public Guid? SourceUserDatabaseId { get; init; }

    public IReadOnlyCollection<Guid>? GameIds { get; init; }

    public GameSelectionFilters Filters { get; init; } = new();
}

/// <summary>Mirrors the filters the games list already applies, so "add" adds exactly what is shown.</summary>
public sealed record GameSelectionFilters
{
    public string? WhiteFirstName { get; init; }
    public string? WhiteLastName { get; init; }
    public string? BlackFirstName { get; init; }
    public string? BlackLastName { get; init; }
    public bool IgnoreColors { get; init; }
    public bool EloEnabled { get; init; }
    public int? EloFrom { get; init; }
    public int? EloTo { get; init; }
    public EloFilterMode EloMode { get; init; } = EloFilterMode.None;
    public bool YearEnabled { get; init; }
    public int? YearFrom { get; init; }
    public int? YearTo { get; init; }
    public string? EcoCode { get; init; }
    public string? Result { get; init; }
    public int? MoveCountFrom { get; init; }
    public int? MoveCountTo { get; init; }
    public bool SearchByPosition { get; init; }
    public string? Fen { get; init; }
    public PositionSearchMode PositionMode { get; init; } = PositionSearchMode.SamePosition;
}

public sealed record AddGamesFromSelectionResponse(
    int AddedCount,
    int SkippedCount,
    int TotalMatched,
    int SavedGamesUsed,
    int SavedGamesLimit);
