namespace ChessXiv.Domain.Entities;

public class Move
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public int MoveNumber { get; set; }
    public string WhiteMove { get; set; } = null!;
    public string? BlackMove { get; set; }
    public string? WhiteClk { get; set; }
    public string? BlackClk { get; set; }
    public double? WhiteEval { get; set; }
    public double? BlackEval { get; set; }
    public Game Game { get; set; } = null!;
}