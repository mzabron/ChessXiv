namespace ChessXiv.Application.Contracts;

public class MoveTreeMoveDto
{
    public string MoveSan { get; set; } = string.Empty;
    public int Games { get; set; }
    public int WhiteWins { get; set; }
    public int Draws { get; set; }
    public int BlackWins { get; set; }
    public decimal WhiteWinPct { get; set; }
    public decimal DrawPct { get; set; }
    public decimal BlackWinPct { get; set; }
}
