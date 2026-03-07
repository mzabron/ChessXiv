using ChessBase.Domain.Engine.Models;

namespace ChessBase.Domain.Engine.Abstractions;

public interface IChessEngine
{
    IReadOnlyList<EngineMove> GetLegalMoves(in BoardState state);

    BoardState MakeMove(in BoardState state, in EngineMove move);
}
