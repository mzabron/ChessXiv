using System.Security.Claims;
using ChessXiv.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChessXiv.Api.Hubs;

[Authorize]
public sealed class ImportProgressHub : Hub
{
    private readonly ImportProgressConnectionRegistry connectionRegistry;

    public ImportProgressHub(ImportProgressConnectionRegistry connectionRegistry)
    {
        this.connectionRegistry = connectionRegistry;
    }

    public const string HubPath = "/hubs/import-progress";

    public override Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier
            ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(userId))
        {
            connectionRegistry.Add(userId, Context.ConnectionId);
        }

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connectionRegistry.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}