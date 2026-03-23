using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Abstractions.Repositories;

public interface IDraftImportRepository
{
    Task<StagingImportSession?> GetImportSessionAsync(Guid importSessionId, string ownerUserId, CancellationToken cancellationToken = default);
    Task<int> CountStagingGamesAsync(Guid importSessionId, string ownerUserId, CancellationToken cancellationToken = default);
    Task DeleteUnpromotedSessionsByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default);
    Task AddImportSessionAsync(StagingImportSession session, CancellationToken cancellationToken = default);
    Task AddStagingGamesAsync(IReadOnlyCollection<StagingGame> games, CancellationToken cancellationToken = default);
}
