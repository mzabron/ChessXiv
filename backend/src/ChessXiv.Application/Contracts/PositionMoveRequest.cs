namespace ChessXiv.Application.Contracts;

public class PositionMoveRequest
{
    public string Fen { get; set; } = string.Empty;
    public string? From { get; set; }
    public string? To { get; set; }
    public string? San { get; set; }
    public string? Promotion { get; set; }
}
