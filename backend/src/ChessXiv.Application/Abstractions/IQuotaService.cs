namespace ChessXiv.Application.Abstractions;

public interface IQuotaService
{
    Task<int> GetMaxDraftImportGamesAsync(string? ownerUserId, CancellationToken cancellationToken = default);
}
