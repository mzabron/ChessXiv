using ChessXiv.Api.Authentication;
using ChessXiv.Api.Authentication;
using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;
using ChessXiv.Application.Exceptions;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ChessXiv.Api.Services;

namespace ChessXiv.Api.Controllers;

[ApiController]
[Route("api/pgn")]
public class PgnImportController(
    IDraftPromotionService draftPromotionService,
    ChessXivDbContext dbContext,
    IBoardStateSerializer boardStateSerializer,
    IPositionKeyCalculator positionKeyCalculator,
    DraftImportProgressCache progressCache,
    IDraftSessionTracker draftSessionTracker,
    IGameReplayBuilder gameReplayBuilder,
    IDraftClaimService draftClaimService,
    BackgroundImportQueue backgroundQueue) : ControllerBase
{
    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpPost("drafts/claim")]
    public async Task<IActionResult> ClaimGuestDraft([FromBody] ClaimGuestDraftRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await draftClaimService.ClaimAsync(request?.GuestToken ?? string.Empty, userId, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpPost("import-to-database-file")]
    [RequestSizeLimit(ChessXivLimits.MaxUploadBytes)]
    public async Task<IActionResult> ImportToDatabaseFile([FromForm] IFormFile file, [FromForm] Guid userDatabaseId, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("File is empty or not provided.");
        }

        if (userDatabaseId == Guid.Empty)
        {
            return BadRequest("User database id is required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var tempFilePath = Path.GetTempFileName();
        await using (var stream = new FileStream(tempFilePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        await backgroundQueue.QueueBackgroundWorkItemAsync(new BackgroundImportJob
        {
            UserId = userId,
            TempFilePath = tempFilePath,
            TargetType = ImportTargetType.UserDatabase,
            UserDatabaseId = userDatabaseId
        });

        return Accepted();
    }

    [Authorize]
    [HttpPost("drafts/import-file")]
    [RequestSizeLimit(ChessXivLimits.MaxUploadBytes)]
    public async Task<IActionResult> ImportDraftFile(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("File is empty or not provided.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var tempFilePath = Path.GetTempFileName();
        await using (var stream = new FileStream(tempFilePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        await backgroundQueue.QueueBackgroundWorkItemAsync(new BackgroundImportJob
        {
            UserId = userId,
            TempFilePath = tempFilePath,
            TargetType = ImportTargetType.Draft
        });

        return Accepted();
    }

    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
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

        try
        {
            var result = await draftPromotionService.PromoteAsync(
                userId,
                request.UserDatabaseId,
                cancellationToken);

            return Ok(result);
        }
        catch (SavedGamesLimitExceededException ex)
        {
            return Conflict(new
            {
                code = "SAVED_GAMES_LIMIT",
                message = $"You can save up to {ex.Limit:N0} games. You already have {ex.CurrentlySaved:N0}, "
                          + $"so there is room for {ex.Remaining:N0} more, but this import has {ex.Requested:N0}.",
                ex.CurrentlySaved,
                ex.Requested,
                ex.Limit,
                ex.Remaining
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
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
        [FromQuery] PositionSearchMode positionMode = PositionSearchMode.SamePosition,
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

        await draftSessionTracker.TouchAsync(userId, cancellationToken);

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
        var positionTarget = PositionSearchTarget.Resolve(searchByPosition, fen, boardStateSerializer, positionKeyCalculator);

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
        query = query.ApplyPositionFilters(searchByPosition, positionTarget?.PosKey, positionMode, positionTarget?.PlyCount);

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

        await draftSessionTracker.TouchAsync(userId, cancellationToken);

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

        var replay = gameReplayBuilder.Build(game.Pgn);

        return Ok(new GameReplayResponse(
            game.Id,
            game.White,
            game.WhiteElo,
            game.Black,
            game.BlackElo,
            game.Result,
            game.Event,
            game.Year,
            replay.FenHistory,
            replay.Moves));
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

        await draftSessionTracker.ClearAsync(userId, cancellationToken);

        return Ok(new { deletedCount });
    }

    private string? GetCurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
    }


    private static string? NormalizeNameToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

}
