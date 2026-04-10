using ChessXiv.Domain.Engine.Models;

namespace ChessXiv.Domain.Engine.Abstractions;

public interface IMoveGenerator
{
    IReadOnlyList<EngineMove> GeneratePseudoLegalMoves(in BoardState state);

    IReadOnlyList<EngineMove> GenerateLegalMoves(in BoardState state);
}
