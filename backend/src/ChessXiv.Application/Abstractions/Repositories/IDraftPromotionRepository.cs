using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Abstractions.Repositories;

public interface IDraftPromotionRepository
{
    Task<UserDatabase?> GetUserDatabaseAsync(Guid userDatabaseId, CancellationToken cancellationToken = default);
    Task<int> PromoteAllAsync(
        string ownerUserId,
        Guid userDatabaseId,
        DateTime addedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Distinct games the owner keeps across all of their databases.</summary>
    Task<int> CountSavedGamesAsync(string ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Games sitting in the owner's draft, i.e. how many a promotion would add.</summary>
    Task<int> CountStagingGamesAsync(string ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes the denormalised game count after a set-based write that bypassed change
    /// tracking, and stamps the database as having changed contents. Every bulk path that
    /// adds or removes games funnels through here, which is what keeps the count and the
    /// "last modified" timestamp honest without scattering that concern across services.
    /// </summary>
    Task SyncGameCountAsync(Guid userDatabaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a chosen subset of the owner's draft into a database and links it, leaving
    /// the draft itself intact. Existing games and links are left untouched, so adding the
    /// same games twice never duplicates them.
    /// </summary>
    Task<int> PromoteSelectionAsync(
        string ownerUserId,
        Guid userDatabaseId,
        IReadOnlyCollection<Guid> stagingGameIds,
        DateTime addedAtUtc,
        CancellationToken cancellationToken = default);
}
