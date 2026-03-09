using ChessBase.Domain.Engine.Models;

namespace ChessBase.Domain.Engine.Abstractions;

public interface IBoardStateTransition
{
    bool TryApplySan(BoardState state, string san);
}
