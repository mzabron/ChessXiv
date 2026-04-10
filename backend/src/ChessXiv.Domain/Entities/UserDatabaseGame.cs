namespace ChessXiv.Domain.Entities;

public class UserDatabaseGame
{
    public Guid UserDatabaseId { get; set; }
    public Guid GameId { get; set; }
    public DateTime AddedAtUtc { get; set; }
    public DateTime? Date { get; set; }
    public int? Year { get; set; }
    public string? Event { get; set; }
    public string? Round { get; set; }
    public string? Site { get; set; }

    public UserDatabase UserDatabase { get; set; } = null!;
    public Game Game { get; set; } = null!;
}
