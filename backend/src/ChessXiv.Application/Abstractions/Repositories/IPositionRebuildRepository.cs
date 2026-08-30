using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Abstractions.Repositories;

public interface IGameSourceRepository
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Streams stored games without materialising the whole table.</summary>
    IAsyncEnumerable<StoredGamePgn> StreamAsync(int batchSize, CancellationToken cancellationToken = default);
}

public sealed record StoredGamePgn(Guid Id, string Pgn, string Result);

public interface IPositionRebuildRepository
{
    /// <summary>Replaces the stored positions for the given games in one transaction.</summary>
    Task ReplacePositionsAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default);
}
