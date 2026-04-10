namespace ChessXiv.Domain.Entities;

public class StagingPosition
{
    public Guid Id { get; set; }
    public Guid StagingGameId { get; set; }
    public string Fen { get; set; } = null!;
    public long FenHash { get; set; }
    public int PlyCount { get; set; }
    public string? LastMove { get; set; }
    public char SideToMove { get; set; }

    public StagingGame Game { get; set; } = null!;
}
