using ChessBase.Domain.Engine.Models;

namespace ChessBase.Domain.Engine.Abstractions;

public interface IBoardStateSerializer
{
    BoardState FromFen(string fen);

    string ToFen(in BoardState state);
}
