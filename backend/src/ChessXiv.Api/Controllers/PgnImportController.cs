using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Repositories;
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
    IDirectDatabaseImportService directDatabaseImportService,
    IDraftPromotionService draftPromotionService,
    ChessXivDbContext dbContext,
    IBoardStateSerializer boardStateSerializer,
    IPositionHasher positionHasher,
    ChessXiv.Api.Services.DraftImportProgressCache progressCache) : ControllerBase
{
    private const string DefaultStartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

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
    [HttpPost("import-to-database")]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> ImportToDatabase([FromBody] DirectImportToDatabaseRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Pgn))
        {
            return BadRequest("PGN content is required.");
        }

        if (request.UserDatabaseId == Guid.Empty)
        {
            return BadRequest("User database id is required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        using var reader = new StringReader(request.Pgn);
        var result = await directDatabaseImportService.ImportToDatabaseAsync(
            reader,
            userId,
            request.UserDatabaseId,
            batchSize: 500,
            cancellationToken: cancellationToken);

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
        [FromQuery] string? whiteFirstName = null,
        [FromQuery] string? whiteLastName = null,
        [FromQuery] string? blackFirstName = null,
        [FromQuery] string? blackLastName = null,
        [FromQuery] bool ignoreColors = false,
        [FromQuery] bool eloEnabled = false,
        [FromQuery] int? eloFrom = null,
        [FromQuery] int? eloTo = null,
        [FromQuery] EloFilterMode eloMode = EloFilterMode.None,
        [FromQuery] bool yearEnabled = false,
        [FromQuery] int? yearFrom = null,
        [FromQuery] int? yearTo = null,
        [FromQuery] string? ecoCode = null,
        [FromQuery] string? result = null,
        [FromQuery] int? moveCountFrom = null,
        [FromQuery] int? moveCountTo = null,
        [FromQuery] bool searchByPosition = false,
        [FromQuery] string? fen = null,
        [FromQuery] PositionSearchMode positionMode = PositionSearchMode.Exact,
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

        if (!Enum.IsDefined(eloMode))
        {
            return BadRequest("Invalid eloMode value.");
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

        var normalizedWhiteFirstName = NormalizeNameToken(whiteFirstName);
        var normalizedWhiteLastName = NormalizeNameToken(whiteLastName);
        var normalizedBlackFirstName = NormalizeNameToken(blackFirstName);
        var normalizedBlackLastName = NormalizeNameToken(blackLastName);
        var normalizedFen = NormalizeFenForSearch(fen);
        var fenHash = TryComputeFenHash(searchByPosition, positionMode, normalizedFen, boardStateSerializer, positionHasher);

        query = query.ApplyPlayerFilters(
            ignoreColors,
            normalizedWhiteFirstName,
            normalizedWhiteLastName,
            normalizedBlackFirstName,
            normalizedBlackLastName);
        query = query.ApplyScalarFilters(
            eloEnabled,
            eloFrom,
            eloTo,
            eloMode,
            yearEnabled,
            yearFrom,
            yearTo,
            ecoCode,
            result,
            moveCountFrom,
            moveCountTo);
        query = query.ApplyPositionFilters(searchByPosition, normalizedFen, fenHash, positionMode);

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

    [Authorize]
    [HttpGet("drafts/games/{gameId:guid}")]
    public async Task<IActionResult> GetDraftGameReplay(Guid gameId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var game = await dbContext.StagingGames
            .AsNoTracking()
            .Where(g => g.Id == gameId && g.OwnerUserId == userId)
            .Select(g => new
            {
                g.Id,
                g.White,
                g.WhiteElo,
                g.Black,
                g.BlackElo,
                g.Result,
                g.Event,
                g.Year,
                g.Pgn
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (game is null)
        {
            return NotFound();
        }

        var moves = await dbContext.StagingMoves
            .AsNoTracking()
            .Where(m => m.StagingGameId == gameId)
            .OrderBy(m => m.MoveNumber)
            .Select(m => new GameReplayMoveDto(
                m.MoveNumber,
                m.WhiteMove,
                m.BlackMove,
                string.IsNullOrWhiteSpace(m.WhiteClk) ? null : m.WhiteClk,
                string.IsNullOrWhiteSpace(m.BlackClk) ? null : m.BlackClk))
            .ToListAsync(cancellationToken);

        var fenHistory = await dbContext.StagingPositions
            .AsNoTracking()
            .Where(p => p.StagingGameId == gameId)
            .OrderBy(p => p.PlyCount)
            .Select(p => p.Fen)
            .ToListAsync(cancellationToken);

        if (fenHistory.Count == 0)
        {
            fenHistory.Add(ResolveStartFen(game.Pgn));
        }

        return Ok(new GameReplayResponse(
            game.Id,
            game.White,
            game.WhiteElo,
            game.Black,
            game.BlackElo,
            game.Result,
            game.Event,
            game.Year,
            fenHistory,
            moves));
    }

    [Authorize]
    [HttpDelete("drafts")]
    public async Task<IActionResult> ClearDraftGames(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var deletedCount = await dbContext.StagingGames
            .Where(g => g.OwnerUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return Ok(new { deletedCount });
    }

    private string? GetCurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
    }

    private static string ResolveStartFen(string? pgn)
    {
        if (!string.IsNullOrWhiteSpace(pgn))
        {
            foreach (var line in pgn.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("[FEN \"", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith("\"]", StringComparison.Ordinal))
                {
                    continue;
                }

                var fen = trimmed[6..^2].Trim();
                if (!string.IsNullOrWhiteSpace(fen))
                {
                    return fen;
                }
            }
        }

        return DefaultStartFen;
    }

    private static string? NormalizeNameToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeFenForSearch(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            return null;
        }

        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6)
        {
            return null;
        }

        return string.Join(' ', parts);
    }

    private static long? TryComputeFenHash(
        bool searchByPosition,
        PositionSearchMode positionMode,
        string? normalizedFen,
        IBoardStateSerializer boardStateSerializer,
        IPositionHasher positionHasher)
    {
        if (!searchByPosition)
        {
            return null;
        }

        if (positionMode != PositionSearchMode.Exact && positionMode != PositionSearchMode.SamePosition)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(normalizedFen))
        {
            return null;
        }

        try
        {
            var state = boardStateSerializer.FromFen(normalizedFen);
            return unchecked((long)positionHasher.Compute(state));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
