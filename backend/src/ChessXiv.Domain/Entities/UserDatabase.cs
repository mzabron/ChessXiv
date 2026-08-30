namespace ChessXiv.Domain.Entities;

public class UserDatabase
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsPublic { get; set; }
    public string OwnerUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When the database's contents last changed - games added or removed. Deliberately not
    /// touched by renames, visibility changes, bookmarking or merely opening the database:
    /// this answers "is there anything new in here?", which is what a reader cares about.
    /// </summary>
    public DateTime ContentUpdatedAtUtc { get; set; }

    /// <summary>
    /// Denormalised count of linked games. Counting UserDatabaseGames per row on every
    /// panel render is a full scan of a multi-million-row link table, so the count is
    /// maintained alongside every link insert/delete instead.
    /// </summary>
    public int GameCount { get; set; }

    public ICollection<UserDatabaseGame> UserDatabaseGames { get; set; } = new List<UserDatabaseGame>();
    public ICollection<UserDatabaseBookmark> Bookmarks { get; set; } = new List<UserDatabaseBookmark>();
}
