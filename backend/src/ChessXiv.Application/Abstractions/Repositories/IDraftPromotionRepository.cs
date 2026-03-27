using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Abstractions.Repositories;

public interface IDraftPromotionRepository
{
    Task<UserDatabase?> GetUserDatabaseAsync(Guid userDatabaseId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<StagingGame>> GetStagingGamesPageAsync(
        string ownerUserId,
        int take,
        CancellationToken cancellationToken = default);
    Task AddGameAsync(Game game, CancellationToken cancellationToken = default);
    Task AddUserDatabaseGameAsync(UserDatabaseGame userDatabaseGame, CancellationToken cancellationToken = default);
    Task RemoveStagingGamesAsync(IReadOnlyCollection<Guid> stagingGameIds, CancellationToken cancellationToken = default);
}
