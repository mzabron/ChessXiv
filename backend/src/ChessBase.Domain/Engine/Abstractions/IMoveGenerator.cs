using ChessBase.Domain.Engine.Models;

namespace ChessBase.Domain.Engine.Abstractions;

public interface IMoveGenerator
{
    IReadOnlyList<EngineMove> GeneratePseudoLegalMoves(in BoardState state);

    IReadOnlyList<EngineMove> GenerateLegalMoves(in BoardState state);
}
