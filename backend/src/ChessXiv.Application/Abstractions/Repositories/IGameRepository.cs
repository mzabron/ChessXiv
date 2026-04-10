using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Abstractions.Repositories;

public interface IGameRepository
{
    Task AddRangeAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default);
}
