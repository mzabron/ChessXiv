using ChessBase.Domain.Entities;

namespace ChessBase.Application.Abstractions;

public interface IPgnParser
{
    IReadOnlyCollection<Game> ParsePgn(string pgn);
}
