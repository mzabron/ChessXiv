using ChessXiv.Domain.Engine.Models;

namespace ChessXiv.Domain.Engine.Abstractions;

public interface IChessEngine
{
    IReadOnlyList<EngineMove> GetLegalMoves(in BoardState state);

    BoardState MakeMove(in BoardState state, in EngineMove move);
}
