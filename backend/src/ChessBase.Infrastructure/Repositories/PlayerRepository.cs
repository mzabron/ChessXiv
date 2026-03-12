using ChessBase.Application.Abstractions.Repositories;
using ChessBase.Domain.Entities;
using ChessBase.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ChessBase.Infrastructure.Repositories;

public class PlayerRepository(ChessBaseDbContext dbContext) : IPlayerRepository
{
    public async Task<IReadOnlyDictionary<string, Player>> GetByNormalizedFullNamesAsync(
        IReadOnlyCollection<string> normalizedFullNames,
        CancellationToken cancellationToken = default)
    {
        if (normalizedFullNames.Count == 0)
        {
            return new Dictionary<string, Player>(StringComparer.Ordinal);
        }

        var players = await dbContext.Players
            .Where(p => normalizedFullNames.Contains(p.NormalizedFullName))
            .ToListAsync(cancellationToken);

        return players.ToDictionary(p => p.NormalizedFullName, StringComparer.Ordinal);
    }

    public async Task AddRangeAsync(IReadOnlyCollection<Player> players, CancellationToken cancellationToken = default)
    {
        if (players.Count == 0)
        {
            return;
        }

        await dbContext.Players.AddRangeAsync(players, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>> SearchIdsAsync(
        string? normalizedFirstName,
        string? normalizedLastName,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Players.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedFirstName))
        {
            var like = $"%{normalizedFirstName}%";
            query = query.Where(p => p.NormalizedFirstName != null && EF.Functions.Like(p.NormalizedFirstName, like));
        }

        if (!string.IsNullOrWhiteSpace(normalizedLastName))
        {
            var like = $"%{normalizedLastName}%";
            query = query.Where(p => p.NormalizedLastName != null && EF.Functions.Like(p.NormalizedLastName, like));
        }

        return await query.Select(p => p.Id).ToListAsync(cancellationToken);
    }
}
