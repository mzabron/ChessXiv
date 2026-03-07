using ChessBase.Domain.Engine.Types;

namespace ChessBase.Domain.Engine.Models;

public readonly record struct EngineMove(
    Square From,
    Square To,
    MoveType MoveType = MoveType.Quiet,
    PieceType Promotion = PieceType.None);
