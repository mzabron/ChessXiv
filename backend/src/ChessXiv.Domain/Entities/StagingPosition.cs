namespace ChessXiv.Domain.Entities;

/// <summary>Staging-area twin of <see cref="Position"/>; see that type for the design notes.</summary>
public class StagingPosition
{
    public Guid StagingGameId { get; set; }

    public short PlyCount { get; set; }

    public byte[] PosKey { get; set; } = [];

    public string? NextMove { get; set; }

    public GameResult Result { get; set; }

    public StagingGame Game { get; set; } = null!;
}
