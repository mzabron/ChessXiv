using ChessXiv.Domain.Engine.Models;

namespace ChessXiv.Domain.Engine.Abstractions;

public interface IBoardStateSerializer
{
    BoardState FromFen(string fen);

    string ToFen(in BoardState state);
}
