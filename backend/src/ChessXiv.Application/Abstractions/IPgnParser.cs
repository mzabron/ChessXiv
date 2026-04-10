using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Abstractions;

public interface IPgnParser
{
    IAsyncEnumerable<Game> ParsePgnAsync(TextReader reader, CancellationToken cancellationToken = default);
}
