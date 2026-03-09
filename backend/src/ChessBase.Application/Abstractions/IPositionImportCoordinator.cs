using ChessBase.Domain.Entities;

namespace ChessBase.Application.Abstractions;

public interface IPositionImportCoordinator
{
    Task PopulateAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken = default);
}
