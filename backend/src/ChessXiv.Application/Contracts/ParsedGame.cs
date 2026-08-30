using ChessXiv.Domain.Engine.Models;
using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Contracts;

/// <summary>
/// One game as it came out of a PGN: the metadata that will be persisted, plus the move
/// list, which is used to replay the game into positions and is then discarded rather
/// than stored (the PGN itself is the record of the moves).
/// </summary>
public sealed class ParsedGame
{
    public required Game Game { get; init; }

    public required IReadOnlyList<ParsedMove> Moves { get; init; }
}
