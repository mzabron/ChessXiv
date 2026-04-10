namespace ChessXiv.Domain.Entities;

public class UserDatabase
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsPublic { get; set; }
    public string OwnerUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<UserDatabaseGame> UserDatabaseGames { get; set; } = new List<UserDatabaseGame>();
    public ICollection<UserDatabaseBookmark> Bookmarks { get; set; } = new List<UserDatabaseBookmark>();
}
