using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ChessXiv.Api.Controllers;

[ApiController]
[Route("api/games/explorer")]
public class GameExplorerController(
    IGameExplorerService gameExplorerService,
    IPositionPlayService positionPlayService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] GameExplorerSearchRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        var userId = GetCurrentUserId();

        try
        {
            var result = await gameExplorerService.SearchAsync(request, userId, cancellationToken);
            return Ok(result);
        }
        catch (ForbiddenException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound("User database was not found.");
        }
    }

    [HttpPost("position/move")]
    public IActionResult ApplyPositionMove([FromBody] PositionMoveRequest request)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        var result = positionPlayService.TryApplyMove(request);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("move-tree")]
    public async Task<IActionResult> MoveTree([FromBody] MoveTreeRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Fen))
        {
            return BadRequest("FEN is required.");
        }

        if (request.Source == MoveTreeSource.UserDatabase && (!request.UserDatabaseId.HasValue || request.UserDatabaseId == Guid.Empty))
        {
            return BadRequest("User database id is required for user database move tree.");
        }

        if (request.Source == MoveTreeSource.StagingSession && (!request.ImportSessionId.HasValue || request.ImportSessionId == Guid.Empty))
        {
            return BadRequest("Import session id is required for staging move tree.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await gameExplorerService.GetMoveTreeAsync(request, userId, cancellationToken);
        return Ok(result);
    }

    private string? GetCurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
    }
}
