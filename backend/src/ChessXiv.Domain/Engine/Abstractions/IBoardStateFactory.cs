using ChessXiv.Domain.Engine.Models;

namespace ChessXiv.Domain.Engine.Abstractions;

public interface IBoardStateFactory
{
    BoardState CreateInitial();

    BoardState CreateFromFenOrInitial(string? fen);
}
