using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Abstractions;

public interface IPositionImportCoordinator
{
    Task PopulateAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default);
}
