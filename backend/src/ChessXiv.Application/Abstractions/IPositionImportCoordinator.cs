using ChessXiv.Application.Contracts;

namespace ChessXiv.Application.Abstractions;

public interface IPositionImportCoordinator
{
    /// <summary>
    /// Replays each parsed game and fills its <see cref="Domain.Entities.Game.Positions"/>.
    /// </summary>
    Task PopulateAsync(IReadOnlyCollection<ParsedGame> games, CancellationToken cancellationToken = default);
}
