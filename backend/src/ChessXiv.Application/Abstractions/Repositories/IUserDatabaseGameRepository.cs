using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Abstractions.Repositories;

public interface IUserDatabaseGameRepository
{
    Task AddRangeAsync(IReadOnlyCollection<UserDatabaseGame> userDatabaseGames, CancellationToken cancellationToken = default);
}
