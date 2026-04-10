using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions;

public interface IPositionPlayService
{
    PositionMoveResponse TryApplyMove(PositionMoveRequest request);
}
