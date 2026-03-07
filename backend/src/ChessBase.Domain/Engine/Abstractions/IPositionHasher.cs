using ChessBase.Domain.Engine.Models;

namespace ChessBase.Domain.Engine.Abstractions;

public interface IPositionHasher
{
    ulong Compute(in BoardState state);
}
