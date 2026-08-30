using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions;

public interface IPgnParser
{
    IAsyncEnumerable<ParsedGame> ParsePgnAsync(TextReader reader, CancellationToken cancellationToken = default);

    /// <summary>Parses an in-memory PGN. Used to rebuild a single game for replay.</summary>
    IReadOnlyCollection<ParsedGame> ParsePgn(string pgn);
}
