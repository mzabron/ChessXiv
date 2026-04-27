namespace ChessXiv.Domain.Entities;

public class StagingMove
{
    public Guid Id { get; set; }
    public Guid StagingGameId { get; set; }
    public int MoveNumber { get; set; }
    public string WhiteMove { get; set; } = null!;
    public string? BlackMove { get; set; }
    public string? WhiteClk { get; set; }
    public string? BlackClk { get; set; }

    public StagingGame Game { get; set; } = null!;
}
