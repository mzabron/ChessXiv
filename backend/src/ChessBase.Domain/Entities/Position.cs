namespace ChessBase.Domain.Entities;

public class Position
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public string Fen { get; set; } = null!;
    public long FenHash { get; set; }
    public int PlyCount { get; set; }
    public string? LastMove { get; set; }
    public char SideToMove { get; set; }
    public Game Game { get; set; } = null!;
}