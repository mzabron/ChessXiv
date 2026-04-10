using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace ChessXiv.Api.Services;

public sealed class SubOrNameIdentifierUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? connection.User?.FindFirstValue("sub");
    }
}