using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChessXiv.Api.Controllers;

[ApiController]
[Route("api/pgn")]
public class PgnImportController(
    IPgnImportService pgnImportService,
    IDraftImportService draftImportService,
    IDraftPromotionService draftPromotionService,
    ChessXivDbContext dbContext,
    ChessXiv.Api.Services.DraftImportProgressCache progressCache) : ControllerBase
{
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] PgnImportRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Pgn))
        {
            return BadRequest("PGN content is required.");
        }

        using var reader = new StringReader(request.Pgn);
        var result = await pgnImportService.ImportAsync(reader, cancellationToken: cancellationToken);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("drafts/import")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ImportDraft([FromBody] DraftImportRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Pgn))
        {
            return BadRequest("PGN content is required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        using var reader = new StringReader(request.Pgn);
        var result = await draftImportService.ImportAsync(
            reader,
            userId,
            batchSize: 200,
            cancellationToken: cancellationToken);

        return Ok(result);
    }

    [Authorize]
    [HttpPost("drafts/promote")]
    public async Task<IActionResult> PromoteDraft(
        [FromBody] DraftPromotionRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.UserDatabaseId == Guid.Empty)
        {
            return BadRequest("User database id is required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await draftPromotionService.PromoteAsync(
            userId,
            request.UserDatabaseId,
            cancellationToken);

        return Ok(result);
    }

    [Authorize]
    [HttpGet("drafts/import-progress")]
    public IActionResult GetDraftImportProgress()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var progress = progressCache.Get(userId);
        if (progress is null)
        {
            return NoContent();
        }

        return Ok(progress);
    }

    [Authorize]
    [HttpGet("drafts/games")]
    public async Task<IActionResult> GetDraftGames(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortDirection = "desc",
        [FromQuery] string resultSortMode = "default",
        CancellationToken cancellationToken = default)
    {
        if (page <= 0)
        {
            return BadRequest("Page must be greater than zero.");
        }

        if (pageSize <= 0 || pageSize > 200)
        {
            return BadRequest("Page size must be between 1 and 200.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var normalizedSortBy = (sortBy ?? "createdAt").Trim().ToLowerInvariant();
        var normalizedSortDirection = (sortDirection ?? "desc").Trim().ToLowerInvariant();
        var normalizedResultSortMode = (resultSortMode ?? "default").Trim().ToLowerInvariant();
        var descending = normalizedSortDirection != "asc";

        var query = dbContext.StagingGames
            .AsNoTracking()
            .Where(g => g.OwnerUserId == userId);

        query = (normalizedSortBy, descending) switch
        {
            ("year", true) => query
                .OrderBy(g => g.Year <= 0 ? 1 : 0)
                .ThenByDescending(g => g.Year)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("year", false) => query
                .OrderBy(g => g.Year <= 0 ? 1 : 0)
                .ThenBy(g => g.Year)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("white", true) => query.OrderByDescending(g => g.White).ThenByDescending(g => g.CreatedAtUtc),
            ("white", false) => query.OrderBy(g => g.White).ThenByDescending(g => g.CreatedAtUtc),
            ("black", true) => query.OrderByDescending(g => g.Black).ThenByDescending(g => g.CreatedAtUtc),
            ("black", false) => query.OrderBy(g => g.Black).ThenByDescending(g => g.CreatedAtUtc),
            ("whiteelo", true) => query
                .OrderBy(g => g.WhiteElo == null ? 1 : 0)
                .ThenByDescending(g => g.WhiteElo)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("whiteelo", false) => query
                .OrderBy(g => g.WhiteElo == null ? 1 : 0)
                .ThenBy(g => g.WhiteElo)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("blackelo", true) => query
                .OrderBy(g => g.BlackElo == null ? 1 : 0)
                .ThenByDescending(g => g.BlackElo)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("blackelo", false) => query
                .OrderBy(g => g.BlackElo == null ? 1 : 0)
                .ThenBy(g => g.BlackElo)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("result", _) when normalizedResultSortMode == "whitefirst" => query
                .OrderBy(g => g.Result == "1-0" ? 0 : g.Result == "0-1" ? 1 : g.Result == "1/2-1/2" ? 2 : 3)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("result", _) when normalizedResultSortMode == "blackfirst" => query
                .OrderBy(g => g.Result == "0-1" ? 0 : g.Result == "1-0" ? 1 : g.Result == "1/2-1/2" ? 2 : 3)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("result", _) when normalizedResultSortMode == "drawfirst" => query
                .OrderBy(g => g.Result == "1/2-1/2" ? 0 : g.Result == "1-0" ? 1 : g.Result == "0-1" ? 2 : 3)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("result", _) => query.OrderByDescending(g => g.CreatedAtUtc).ThenByDescending(g => g.Id),
            ("eco", true) => query
                .OrderBy(g => g.ECO == null || g.ECO == "" || g.ECO == "?" ? 1 : 0)
                .ThenByDescending(g => g.ECO)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("eco", false) => query
                .OrderBy(g => g.ECO == null || g.ECO == "" || g.ECO == "?" ? 1 : 0)
                .ThenBy(g => g.ECO)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("event", true) => query
                .OrderBy(g => g.Event == null || g.Event == "" || g.Event == "?" || g.Event == "-" ? 1 : 0)
                .ThenByDescending(g => g.Event)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("event", false) => query
                .OrderBy(g => g.Event == null || g.Event == "" || g.Event == "?" || g.Event == "-" ? 1 : 0)
                .ThenBy(g => g.Event)
                .ThenByDescending(g => g.CreatedAtUtc),
            ("moves", true) => query.OrderByDescending(g => g.MoveCount).ThenByDescending(g => g.CreatedAtUtc),
            ("moves", false) => query.OrderBy(g => g.MoveCount).ThenByDescending(g => g.CreatedAtUtc),
            (_, false) => query.OrderBy(g => g.CreatedAtUtc).ThenBy(g => g.Id),
            _ => query.OrderByDescending(g => g.CreatedAtUtc).ThenByDescending(g => g.Id)
        };

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new DraftGameListItem(
                g.Id,
                g.Year,
                g.White,
                g.WhiteElo,
                g.Result,
                g.Black,
                g.BlackElo,
                g.ECO,
                g.Event,
                g.MoveCount,
                g.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(new DraftGamesPageResponse(page, pageSize, totalCount, items));
    }

    private string? GetCurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
    }
}
