using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;

namespace ChessXiv.Infrastructure.Repositories;

public sealed class UserDatabaseGameRepository(ChessXivDbContext dbContext) : IUserDatabaseGameRepository
{
    public async Task AddRangeAsync(IReadOnlyCollection<UserDatabaseGame> userDatabaseGames, CancellationToken cancellationToken = default)
    {
        if (userDatabaseGames.Count == 0)
        {
            return;
        }

        await dbContext.UserDatabaseGames.AddRangeAsync(userDatabaseGames, cancellationToken);
    }
}
