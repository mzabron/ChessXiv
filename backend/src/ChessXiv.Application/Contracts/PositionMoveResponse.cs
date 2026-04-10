namespace ChessXiv.Application.Contracts;

public class PositionMoveResponse
{
    public bool IsValid { get; set; }
    public string? Fen { get; set; }
    public string? San { get; set; }
    public string? Error { get; set; }
}
