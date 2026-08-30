namespace ChessXiv.Domain.Engine.Models;

/// <summary>
/// A move pair as it appears in a PGN. This is parse output, not stored data: the moves of
/// a game are recoverable from the game's PGN, so keeping a Moves table alongside it was
/// pure duplication. Mutable because the parser fills in the black move and the clock
/// annotations after the row is created.
/// </summary>
public sealed class ParsedMove
{
    public int MoveNumber { get; set; }
    public string WhiteMove { get; set; } = string.Empty;
    public string? BlackMove { get; set; }
    public string? WhiteClk { get; set; }
    public string? BlackClk { get; set; }
}
