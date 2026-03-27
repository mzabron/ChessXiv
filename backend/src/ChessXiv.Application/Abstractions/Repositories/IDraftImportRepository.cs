using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Abstractions.Repositories;

public interface IDraftImportRepository
{
    Task ClearStagingGamesAsync(string ownerUserId, CancellationToken cancellationToken = default);
    Task AddStagingGamesAsync(IReadOnlyCollection<StagingGame> games, CancellationToken cancellationToken = default);
}
