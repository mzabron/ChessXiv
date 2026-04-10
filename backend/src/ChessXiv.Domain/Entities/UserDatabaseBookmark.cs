namespace ChessXiv.Domain.Entities;

public class UserDatabaseBookmark
{
    public string UserId { get; set; } = null!;
    public Guid UserDatabaseId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public UserDatabase UserDatabase { get; set; } = null!;
}
