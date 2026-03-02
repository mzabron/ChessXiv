namespace ChessBase.Domain.Entities;

public class Game
{
    public Guid Id { get; set; }
    public DateTime? Date { get; set; }
    public string? Round { get; set; }
    public int? WhiteRating { get; set; }
    public int? BlackRating { get; set; }
    public string? Event { get; set; }
    public string? Site { get; set; }
    public string? TimeControl { get; set; }
    public string White { get; set; } = null!;
    public string Black { get; set; } = null!;
    public string Result { get; set; } = null!;
    public string ECO { get; set; } = null!;
    public string Pgn { get; set; } = null!;
    public bool IsMaster { get; set; }
    public Guid? CollectionId { get; set; }
    public string? UserId { get; set; }
}