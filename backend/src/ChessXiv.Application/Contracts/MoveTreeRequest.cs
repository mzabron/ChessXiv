namespace ChessXiv.Application.Contracts;

public class MoveTreeRequest
{
    public string? Fen { get; set; }
    public MoveTreeSource Source { get; set; } = MoveTreeSource.UserDatabase;
    public Guid? UserDatabaseId { get; set; }
    public int MaxMoves { get; set; } = 20;
}
