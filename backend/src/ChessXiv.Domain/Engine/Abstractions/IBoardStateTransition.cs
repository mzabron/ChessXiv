using ChessXiv.Domain.Engine.Models;

namespace ChessXiv.Domain.Engine.Abstractions;

public interface IBoardStateTransition
{
    bool TryApplySan(BoardState state, string san);
}
