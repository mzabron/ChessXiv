namespace ChessXiv.Domain.Entities;

/// <summary>
/// One position occurring in a game, stored for position search and the opening tree.
/// </summary>
/// <remarks>
/// Deliberately narrow. The FEN string and its index used to dominate this table's size,
/// so identity is a 16-byte <see cref="PosKey"/> instead, and FENs needed for replay are
/// recomputed from the game's PGN on demand.
///
/// <see cref="NextMove"/> holds the move played *from* this position rather than the one
/// that led *to* it. That shift is what lets the opening tree read a position's
/// continuations straight out of one index range, instead of self-joining Positions to
/// itself on ply + 1.
/// </remarks>
public class Position
{
    public Guid GameId { get; set; }

    public short PlyCount { get; set; }

    /// <summary>128-bit position identity; see IPositionKeyCalculator.</summary>
    public byte[] PosKey { get; set; } = [];

    /// <summary>SAN of the move played from this position, or null at the end of the game.</summary>
    public string? NextMove { get; set; }

    /// <summary>
    /// The owning game's result, denormalised so the opening tree can aggregate wins,
    /// draws and losses from the index alone without touching Games.
    /// </summary>
    public GameResult Result { get; set; }

    public Game Game { get; set; } = null!;
}

/// <summary>Compact form of the PGN result tag.</summary>
public enum GameResult : byte
{
    Unknown = 0,
    WhiteWin = 1,
    Draw = 2,
    BlackWin = 3
}
