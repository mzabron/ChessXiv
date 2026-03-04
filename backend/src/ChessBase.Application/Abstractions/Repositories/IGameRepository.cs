using ChessBase.Domain.Entities;

namespace ChessBase.Application.Abstractions.Repositories;

public interface IGameRepository
{
    Task AddRangeAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default);
}
