namespace ChessBase.Application.Contracts;

public class GameExplorerItemDto
{
    public Guid GameId { get; set; }
    public int? Year { get; set; }
    public string White { get; set; } = string.Empty;
    public int? WhiteElo { get; set; }
    public string Result { get; set; } = string.Empty;
    public string Black { get; set; } = string.Empty;
    public int? BlackElo { get; set; }
    public string? Eco { get; set; }
    public string? Tournament { get; set; }
    public int MoveCount { get; set; }
}
