using ChessXiv.Domain.Engine.Models;

namespace ChessXiv.Domain.Engine.Abstractions;

public interface IPositionHasher
{
    ulong Compute(in BoardState state);
}
