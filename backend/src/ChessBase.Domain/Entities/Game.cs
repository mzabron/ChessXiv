namespace ChessBase.Domain.Entities;

public class Game
{
    public Guid Id { get; set; }
    public Guid? WhitePlayerId { get; set; }
    public Guid? BlackPlayerId { get; set; }
    public DateTime? Date { get; set; }
    public int Year { get; set; }
    public string? Round { get; set; }
    public string? WhiteTitle { get; set; }
    public string? BlackTitle { get; set; }
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    public string? Event { get; set; }
    public string? Site { get; set; }
    public string? TimeControl { get; set; }
    public string? ECO { get; set; }
    public string? Opening { get; set; }
    public string White { get; set; } = null!;
    public string Black { get; set; } = null!;
    public string Result { get; set; } = null!;
    public string Pgn { get; set; } = null!;
    public int MoveCount { get; set; }
    public bool IsMaster { get; set; } = false;
    public Guid? CollectionId { get; set; }
    public string? UserId { get; set; }
    public Player? WhitePlayer { get; set; }
    public Player? BlackPlayer { get; set; }
    public ICollection<Move> Moves { get; set; } = new List<Move>();
    public ICollection<Position> Positions { get; set; } = new List<Position>();
}