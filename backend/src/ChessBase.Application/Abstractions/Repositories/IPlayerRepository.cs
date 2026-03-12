using ChessBase.Domain.Entities;

namespace ChessBase.Application.Abstractions.Repositories;

public interface IPlayerRepository
{
    Task<IReadOnlyDictionary<string, Player>> GetByNormalizedFullNamesAsync(
        IReadOnlyCollection<string> normalizedFullNames,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<Player> players, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> SearchIdsAsync(
        string? normalizedFirstName,
        string? normalizedLastName,
        CancellationToken cancellationToken = default);
}
