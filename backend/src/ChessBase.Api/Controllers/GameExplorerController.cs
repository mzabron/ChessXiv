using ChessBase.Application.Abstractions;
using ChessBase.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ChessBase.Api.Controllers;

[ApiController]
[Route("api/games/explorer")]
public class GameExplorerController(IGameExplorerService gameExplorerService) : ControllerBase
{
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] GameExplorerSearchRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        var result = await gameExplorerService.SearchAsync(request, cancellationToken);
        return Ok(result);
    }
}
