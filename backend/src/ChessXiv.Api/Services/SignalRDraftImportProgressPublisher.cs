using ChessXiv.Api.Hubs;
using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace ChessXiv.Api.Services;

public sealed class SignalRDraftImportProgressPublisher(
    IHubContext<ImportProgressHub> hubContext,
    DraftImportProgressCache progressCache,
    ImportProgressConnectionRegistry connectionRegistry) : IDraftImportProgressPublisher
{
    public Task PublishAsync(string ownerUserId, DraftImportProgressUpdate update, CancellationToken cancellationToken = default)
    {
        progressCache.Set(ownerUserId, update);

        var connectionIds = connectionRegistry.GetConnectionIds(ownerUserId);
        if (connectionIds.Count > 0)
        {
            return hubContext.Clients
                .Clients(connectionIds)
                .SendAsync("draftImportProgress", update, cancellationToken);
        }

        return hubContext.Clients
            .User(ownerUserId)
            .SendAsync("draftImportProgress", update, cancellationToken);
    }
}