namespace ChessXiv.Domain.Entities;

public class Game
{
    public Guid Id { get; set; }
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
    public string WhiteNormalizedFullName { get; set; } = string.Empty;
    public string? WhiteNormalizedFirstName { get; set; }
    public string? WhiteNormalizedLastName { get; set; }
    public string BlackNormalizedFullName { get; set; } = string.Empty;
    public string? BlackNormalizedFirstName { get; set; }
    public string? BlackNormalizedLastName { get; set; }
    public string Result { get; set; } = null!;
    public string Pgn { get; set; } = null!;
    public int MoveCount { get; set; }
    public string GameHash { get; set; } = string.Empty;
    public bool IsMaster { get; set; } = false;
    public ICollection<Move> Moves { get; set; } = new List<Move>();
    public ICollection<Position> Positions { get; set; } = new List<Position>();
    public ICollection<UserDatabaseGame> UserDatabaseGames { get; set; } = new List<UserDatabaseGame>();
}