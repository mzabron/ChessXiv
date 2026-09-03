using System.Security.Claims;
using ChessXiv.Api.Authentication;
using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Api.Controllers;

[ApiController]
[Route("api/user-databases")]
public class UserDatabasesController(
    ChessXivDbContext dbContext,
    IDraftPromotionRepository draftPromotionRepository,
    IGameReplayBuilder gameReplayBuilder,
    IBoardStateSerializer boardStateSerializer,
    IPositionKeyCalculator positionKeyCalculator) : ControllerBase
{
    /// <summary>
    /// Lists every database the caller may open: all public ones plus, when signed in,
    /// the caller's own private ones. Anonymous and authenticated callers see the same
    /// public set - signing in only ever adds rows, never removes them.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetVisible(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var bookmarkedIds = userId is null
            ? []
            : await dbContext.UserDatabaseBookmarks
                .AsNoTracking()
                .Where(b => b.UserId == userId)
                .Select(b => b.UserDatabaseId)
                .ToListAsync(cancellationToken);

        var bookmarkedIdSet = bookmarkedIds.ToHashSet();

        var items = await dbContext.UserDatabases
            .AsNoTracking()
            .Where(d => d.IsPublic || (userId != null && d.OwnerUserId == userId))
            .OrderBy(d => d.Name)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.IsPublic,
                d.OwnerUserId,
                OwnerUserName = dbContext.Users
                    .Where(u => u.Id == d.OwnerUserId)
                    .Select(u => u.UserName)
                    .FirstOrDefault(),
                d.GameCount,
                d.CreatedAtUtc,
                d.ContentUpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var response = items
            .Select(d => new UserDatabaseListItemDto(
                d.Id,
                d.Name,
                d.IsPublic,
                d.OwnerUserId,
                d.OwnerUserName ?? d.OwnerUserId,
                d.GameCount,
                d.CreatedAtUtc,
                d.ContentUpdatedAtUtc,
                IsOwner: userId != null && d.OwnerUserId == userId,
                IsBookmarked: bookmarkedIdSet.Contains(d.Id)))
            .ToList();

        return Ok(response);
    }


    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var dto = await dbContext.UserDatabases
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new UserDatabaseDto(
                d.Id,
                d.Name,
                d.IsPublic,
                d.OwnerUserId,
                dbContext.Users
                    .Where(u => u.Id == d.OwnerUserId)
                    .Select(u => u.UserName)
                    .FirstOrDefault() ?? d.OwnerUserId,
                d.GameCount,
                d.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        if (dto is null)
        {
            return NotFound();
        }

        if (!dto.IsPublic && dto.OwnerUserId != userId)
        {
            return Forbid();
        }

        return Ok(dto);
    }

    [HttpGet("{id:guid}/games")]
    public async Task<IActionResult> GetGames(
        Guid id,
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

        var dbInfo = await dbContext.UserDatabases
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new
            {
                d.Id,
                d.OwnerUserId,
                d.IsPublic,
                d.GameCount
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (dbInfo is null)
        {
            return NotFound();
        }

        if (!dbInfo.IsPublic && dbInfo.OwnerUserId != userId)
        {
            return Forbid();
        }

        var normalizedSortBy = (sortBy ?? "createdAt").Trim().ToLowerInvariant();
        var normalizedSortDirection = (sortDirection ?? "desc").Trim().ToLowerInvariant();
        var normalizedResultSortMode = (resultSortMode ?? "default").Trim().ToLowerInvariant();
        var descending = normalizedSortDirection != "asc";

        var query = dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabaseId == id);

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
                .OrderBy(x => x.Game.Year <= 0 ? 1 : 0)
                .ThenByDescending(x => x.Game.Year)
                .ThenByDescending(x => x.AddedAtUtc),
            ("year", false) => query
                .OrderBy(x => x.Game.Year <= 0 ? 1 : 0)
                .ThenBy(x => x.Game.Year)
                .ThenByDescending(x => x.AddedAtUtc),
            ("white", true) => query.OrderByDescending(x => x.Game.White).ThenByDescending(x => x.AddedAtUtc),
            ("white", false) => query.OrderBy(x => x.Game.White).ThenByDescending(x => x.AddedAtUtc),
            ("black", true) => query.OrderByDescending(x => x.Game.Black).ThenByDescending(x => x.AddedAtUtc),
            ("black", false) => query.OrderBy(x => x.Game.Black).ThenByDescending(x => x.AddedAtUtc),
            ("whiteelo", true) => query
                .OrderBy(x => x.Game.WhiteElo == null ? 1 : 0)
                .ThenByDescending(x => x.Game.WhiteElo)
                .ThenByDescending(x => x.AddedAtUtc),
            ("whiteelo", false) => query
                .OrderBy(x => x.Game.WhiteElo == null ? 1 : 0)
                .ThenBy(x => x.Game.WhiteElo)
                .ThenByDescending(x => x.AddedAtUtc),
            ("blackelo", true) => query
                .OrderBy(x => x.Game.BlackElo == null ? 1 : 0)
                .ThenByDescending(x => x.Game.BlackElo)
                .ThenByDescending(x => x.AddedAtUtc),
            ("blackelo", false) => query
                .OrderBy(x => x.Game.BlackElo == null ? 1 : 0)
                .ThenBy(x => x.Game.BlackElo)
                .ThenByDescending(x => x.AddedAtUtc),
            ("result", _) when normalizedResultSortMode == "whitefirst" => query
                .OrderBy(x => x.Game.Result == "1-0" ? 0 : x.Game.Result == "0-1" ? 1 : x.Game.Result == "1/2-1/2" ? 2 : 3)
                .ThenByDescending(x => x.AddedAtUtc),
            ("result", _) when normalizedResultSortMode == "blackfirst" => query
                .OrderBy(x => x.Game.Result == "0-1" ? 0 : x.Game.Result == "1-0" ? 1 : x.Game.Result == "1/2-1/2" ? 2 : 3)
                .ThenByDescending(x => x.AddedAtUtc),
            ("result", _) when normalizedResultSortMode == "drawfirst" => query
                .OrderBy(x => x.Game.Result == "1/2-1/2" ? 0 : x.Game.Result == "1-0" ? 1 : x.Game.Result == "0-1" ? 2 : 3)
                .ThenByDescending(x => x.AddedAtUtc),
            ("result", _) => query.OrderByDescending(x => x.AddedAtUtc).ThenByDescending(x => x.Game.Id),
            ("eco", true) => query
                .OrderBy(x => x.Game.ECO == null || x.Game.ECO == "" || x.Game.ECO == "?" ? 1 : 0)
                .ThenByDescending(x => x.Game.ECO)
                .ThenByDescending(x => x.AddedAtUtc),
            ("eco", false) => query
                .OrderBy(x => x.Game.ECO == null || x.Game.ECO == "" || x.Game.ECO == "?" ? 1 : 0)
                .ThenBy(x => x.Game.ECO)
                .ThenByDescending(x => x.AddedAtUtc),
            ("event", true) => query
                .OrderBy(x => x.Game.Event == null || x.Game.Event == "" || x.Game.Event == "?" || x.Game.Event == "-" ? 1 : 0)
                .ThenByDescending(x => x.Game.Event)
                .ThenByDescending(x => x.AddedAtUtc),
            ("event", false) => query
                .OrderBy(x => x.Game.Event == null || x.Game.Event == "" || x.Game.Event == "?" || x.Game.Event == "-" ? 1 : 0)
                .ThenBy(x => x.Game.Event)
                .ThenByDescending(x => x.AddedAtUtc),
            ("moves", true) => query.OrderByDescending(x => x.Game.MoveCount).ThenByDescending(x => x.AddedAtUtc),
            ("moves", false) => query.OrderBy(x => x.Game.MoveCount).ThenByDescending(x => x.AddedAtUtc),
            // GameId, not Game.Id: identical value, but reading it off the link means the
            // default ordering never has to touch Games, so it can be served straight from
            // IX_UserDatabaseGames_UserDatabaseId_AddedAtUtc_GameId.
            (_, false) => query.OrderBy(x => x.AddedAtUtc).ThenBy(x => x.GameId),
            _ => query.OrderByDescending(x => x.AddedAtUtc).ThenByDescending(x => x.GameId)
        };

        // An unfiltered list is the whole database, and UserDatabases.GameCount already
        // holds that number - counting 800k+ link rows on every page turn to rediscover it
        // is the single most wasteful part of this endpoint.
        var hasNarrowingFilters =
            !string.IsNullOrWhiteSpace(normalizedWhiteFirstName) ||
            !string.IsNullOrWhiteSpace(normalizedWhiteLastName) ||
            !string.IsNullOrWhiteSpace(normalizedBlackFirstName) ||
            !string.IsNullOrWhiteSpace(normalizedBlackLastName) ||
            eloEnabled ||
            yearEnabled ||
            !string.IsNullOrWhiteSpace(ecoCode) ||
            !string.IsNullOrWhiteSpace(result) ||
            moveCountFrom.HasValue ||
            moveCountTo.HasValue ||
            positionTarget is not null;

        var totalCount = hasNarrowingFilters
            ? await query.CountAsync(cancellationToken)
            : dbInfo.GameCount;

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new DraftGameListItem(
                x.Game.Id,
                x.Game.Year,
                x.Game.White,
                x.Game.WhiteElo,
                x.Game.Result,
                x.Game.Black,
                x.Game.BlackElo,
                x.Game.ECO,
                x.Game.Event,
                x.Game.MoveCount,
                x.AddedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(new DraftGamesPageResponse(page, pageSize, totalCount, items));
    }

    [HttpGet("{id:guid}/games/{gameId:guid}")]
    public async Task<IActionResult> GetGameReplay(Guid id, Guid gameId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var dbInfo = await dbContext.UserDatabases
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new
            {
                d.OwnerUserId,
                d.IsPublic
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (dbInfo is null)
        {
            return NotFound();
        }

        if (!dbInfo.IsPublic && dbInfo.OwnerUserId != userId)
        {
            return Forbid();
        }

        var linked = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .AnyAsync(x => x.UserDatabaseId == id && x.GameId == gameId, cancellationToken);

        if (!linked)
        {
            return NotFound();
        }

        var game = await dbContext.Games
            .AsNoTracking()
            .Where(g => g.Id == gameId)
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

    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserDatabaseRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Database name is required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var normalizedName = request.Name.Trim();
        var exists = await dbContext.UserDatabases
            .AnyAsync(d => d.OwnerUserId == userId && d.Name == normalizedName, cancellationToken);

        if (exists)
        {
            return Conflict("A database with this name already exists for this user.");
        }

        var createdAtUtc = DateTime.UtcNow;
        var entity = new UserDatabase
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            IsPublic = request.IsPublic,
            OwnerUserId = userId,
            CreatedAtUtc = createdAtUtc,
            ContentUpdatedAtUtc = createdAtUtc
        };

        dbContext.UserDatabases.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var ownerUserName = await dbContext.Users
            .Where(u => u.Id == entity.OwnerUserId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync(cancellationToken)
            ?? entity.OwnerUserId;

        var dto = new UserDatabaseDto(entity.Id, entity.Name, entity.IsPublic, entity.OwnerUserId, ownerUserName, 0, entity.CreatedAtUtc);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, dto);
    }

    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDatabaseRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Database name is required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var entity = await dbContext.UserDatabases.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (entity.OwnerUserId != userId)
        {
            return Forbid();
        }

        var normalizedName = request.Name.Trim();
        var duplicate = await dbContext.UserDatabases
            .AnyAsync(d => d.OwnerUserId == userId && d.Name == normalizedName && d.Id != id, cancellationToken);

        if (duplicate)
        {
            return Conflict("A database with this name already exists for this user.");
        }

        entity.Name = normalizedName;
        entity.IsPublic = request.IsPublic;

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var entity = await dbContext.UserDatabases.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (entity.OwnerUserId != userId)
        {
            return Forbid();
        }

        // Ensure long-running deletes complete even if the client disconnects.
        var deleteToken = CancellationToken.None;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(deleteToken);

        var linkedGameIds = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(x => x.UserDatabaseId == id)
            .Select(x => x.GameId)
            .Distinct()
            .ToArrayAsync(deleteToken);

        await dbContext.UserDatabaseGames
            .Where(x => x.UserDatabaseId == id)
            .ExecuteDeleteAsync(deleteToken);

        await dbContext.UserDatabases
            .Where(d => d.Id == id)
            .ExecuteDeleteAsync(deleteToken);

        if (linkedGameIds.Length > 0)
        {
            const int batchSize = 500;

            for (var i = 0; i < linkedGameIds.Length; i += batchSize)
            {
                var batch = linkedGameIds
                    .Skip(i)
                    .Take(batchSize)
                    .ToArray();

                var orphanIds = await (
                    from game in dbContext.Games.AsNoTracking()
                    where batch.Contains(game.Id)
                    join link in dbContext.UserDatabaseGames.AsNoTracking() on game.Id equals link.GameId into gameLinks
                    from gameLink in gameLinks.DefaultIfEmpty()
                    where gameLink == null
                    select game.Id)
                    .ToArrayAsync(deleteToken);

                if (orphanIds.Length == 0)
                {
                    continue;
                }

                await dbContext.Games
                    .Where(g => orphanIds.Contains(g.Id))
                    .ExecuteDeleteAsync(deleteToken);
            }
        }

        await transaction.CommitAsync(deleteToken);

        return NoContent();
    }

    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpPost("{id:guid}/bookmark")]
    public async Task<IActionResult> AddBookmark(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var dbEntity = await dbContext.UserDatabases
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new { d.Id, d.IsPublic, d.OwnerUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (dbEntity is null)
        {
            return NotFound();
        }

        if (!dbEntity.IsPublic && dbEntity.OwnerUserId != userId)
        {
            return Forbid();
        }

        var alreadyExists = await dbContext.UserDatabaseBookmarks
            .AnyAsync(x => x.UserId == userId && x.UserDatabaseId == id, cancellationToken);

        if (alreadyExists)
        {
            return Ok(new { IsBookmarked = true, Created = false });
        }

        dbContext.UserDatabaseBookmarks.Add(new UserDatabaseBookmark
        {
            UserId = userId,
            UserDatabaseId = id,
            CreatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { IsBookmarked = true, Created = true });
    }

    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpDelete("{id:guid}/bookmark")]
    public async Task<IActionResult> RemoveBookmark(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var bookmark = await dbContext.UserDatabaseBookmarks
            .FirstOrDefaultAsync(x => x.UserId == userId && x.UserDatabaseId == id, cancellationToken);

        if (bookmark is null)
        {
            return NoContent();
        }

        dbContext.UserDatabaseBookmarks.Remove(bookmark);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpPost("{id:guid}/games")]
    public async Task<IActionResult> AddGames(Guid id, [FromBody] AddGamesToDatabaseRequest request, CancellationToken cancellationToken)
    {
        if (request?.GameIds is null || request.GameIds.Count == 0)
        {
            return BadRequest("At least one game id is required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var dbEntity = await dbContext.UserDatabases.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dbEntity is null)
        {
            return NotFound();
        }

        if (dbEntity.OwnerUserId != userId)
        {
            return Forbid();
        }

        var distinctGameIds = request.GameIds.Where(g => g != Guid.Empty).Distinct().ToArray();
        if (distinctGameIds.Length == 0)
        {
            return BadRequest("Provided game ids are invalid.");
        }

        var existingGames = await dbContext.Games
            .Where(g => distinctGameIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Date, g.Year, g.Event, g.Round, g.Site })
            .ToListAsync(cancellationToken);

        var existingGameIds = existingGames.Select(g => g.Id).ToArray();

        var missing = distinctGameIds.Except(existingGameIds).ToArray();
        if (missing.Length > 0)
        {
            return NotFound(new { MissingGameIds = missing });
        }

        var alreadyLinked = await dbContext.UserDatabaseGames
            .Where(x => x.UserDatabaseId == id && distinctGameIds.Contains(x.GameId))
            .Select(x => x.GameId)
            .ToListAsync(cancellationToken);

        var existingGameMap = existingGames.ToDictionary(g => g.Id);

        var toInsert = distinctGameIds.Except(alreadyLinked)
            .Select(gameId =>
            {
                var game = existingGameMap[gameId];
                return new UserDatabaseGame
                {
                    UserDatabaseId = id,
                    GameId = gameId,
                    AddedAtUtc = DateTime.UtcNow,
                    Date = game.Date,
                    Year = game.Year,
                    Event = game.Event,
                    Round = game.Round,
                    Site = game.Site
                };
            })
            .ToArray();

        if (toInsert.Length > 0)
        {
            dbContext.UserDatabaseGames.AddRange(toInsert);
            dbEntity.GameCount += toInsert.Length;
            dbEntity.ContentUpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            AddedCount = toInsert.Length,
            SkippedCount = alreadyLinked.Count
        });
    }

    /// <summary>
    /// Adds games to this database from the caller's draft or from another database they
    /// can read. Without an explicit id list the whole filtered result set is added, not
    /// just the visible page. Games already linked here are skipped rather than duplicated.
    /// </summary>
    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpPost("{id:guid}/games/from-selection")]
    public async Task<IActionResult> AddGamesFromSelection(
        Guid id,
        [FromBody] AddGamesFromSelectionRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (!Enum.IsDefined(request.Filters.EloMode) || !Enum.IsDefined(request.Filters.PositionMode))
        {
            return BadRequest("Invalid filter value.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var target = await dbContext.UserDatabases.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (target is null)
        {
            return NotFound();
        }

        if (target.OwnerUserId != userId)
        {
            return Forbid();
        }

        var candidateIds = await ResolveSelectedGameIdsAsync(request, userId, cancellationToken);
        if (candidateIds is null)
        {
            return Forbid();
        }

        var totalMatched = candidateIds.Count;

        var alreadyLinked = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabaseId == id && candidateIds.Contains(link.GameId))
            .Select(link => link.GameId)
            .ToListAsync(cancellationToken);

        var alreadyLinkedSet = alreadyLinked.ToHashSet();
        var toAdd = candidateIds.Where(gameId => !alreadyLinkedSet.Contains(gameId)).ToArray();

        var savedGamesUsed = await draftPromotionRepository.CountSavedGamesAsync(userId, cancellationToken);
        var remaining = ChessXivLimits.MaxSavedGamesPerUser - savedGamesUsed;

        if (toAdd.Length > remaining)
        {
            return Conflict(new
            {
                code = "SAVED_GAMES_LIMIT",
                message = $"You can save up to {ChessXivLimits.MaxSavedGamesPerUser:N0} games. "
                          + $"You already have {savedGamesUsed:N0}, so there is room for {Math.Max(0, remaining):N0} more, "
                          + $"but this selection adds {toAdd.Length:N0}.",
                CurrentlySaved = savedGamesUsed,
                Requested = toAdd.Length,
                Limit = ChessXivLimits.MaxSavedGamesPerUser,
                Remaining = Math.Max(0, remaining)
            });
        }

        if (toAdd.Length > 0)
        {
            if (request.SourceUserDatabaseId.HasValue)
            {
                await LinkExistingGamesAsync(id, toAdd, cancellationToken);
            }
            else
            {
                // Draft games do not live in Games yet, so they are copied across first.
                await draftPromotionRepository.PromoteSelectionAsync(
                    userId,
                    id,
                    toAdd,
                    DateTime.UtcNow,
                    cancellationToken);
            }

            await draftPromotionRepository.SyncGameCountAsync(id, cancellationToken);
        }

        return Ok(new AddGamesFromSelectionResponse(
            AddedCount: toAdd.Length,
            SkippedCount: alreadyLinked.Count,
            TotalMatched: totalMatched,
            SavedGamesUsed: await draftPromotionRepository.CountSavedGamesAsync(userId, cancellationToken),
            SavedGamesLimit: ChessXivLimits.MaxSavedGamesPerUser));
    }

    private async Task LinkExistingGamesAsync(Guid userDatabaseId, IReadOnlyCollection<Guid> gameIds, CancellationToken cancellationToken)
    {
        var addedAtUtc = DateTime.UtcNow;
        var idArray = gameIds as Guid[] ?? gameIds.ToArray();

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "UserDatabaseGames" (
                "UserDatabaseId", "GameId", "AddedAtUtc", "Date", "Year", "Event", "Round", "Site"
            )
            SELECT
                {userDatabaseId}, g."Id", {addedAtUtc}, g."Date",
                CASE WHEN g."Year" <= 0 THEN NULL ELSE g."Year" END,
                g."Event", g."Round", g."Site"
            FROM "Games" g
            WHERE g."Id" = ANY({idArray})
            ON CONFLICT ("UserDatabaseId", "GameId") DO NOTHING;
            """, cancellationToken);
    }

    /// <summary>
    /// Resolves the games the request refers to. Returns null when the caller may not read
    /// the source database.
    /// </summary>
    private async Task<List<Guid>?> ResolveSelectedGameIdsAsync(
        AddGamesFromSelectionRequest request,
        string userId,
        CancellationToken cancellationToken)
    {
        var explicitIds = request.GameIds?.Where(g => g != Guid.Empty).Distinct().ToList();
        var filters = request.Filters;

        var positionTarget = PositionSearchTarget.Resolve(
            filters.SearchByPosition,
            filters.Fen,
            boardStateSerializer,
            positionKeyCalculator);

        if (request.SourceUserDatabaseId.HasValue)
        {
            var sourceId = request.SourceUserDatabaseId.Value;
            var sourceInfo = await dbContext.UserDatabases
                .AsNoTracking()
                .Where(d => d.Id == sourceId)
                .Select(d => new { d.OwnerUserId, d.IsPublic })
                .FirstOrDefaultAsync(cancellationToken);

            if (sourceInfo is null || (!sourceInfo.IsPublic && sourceInfo.OwnerUserId != userId))
            {
                return null;
            }

            var linkQuery = dbContext.UserDatabaseGames
                .AsNoTracking()
                .Where(link => link.UserDatabaseId == sourceId);

            if (explicitIds is { Count: > 0 })
            {
                return await linkQuery
                    .Where(link => explicitIds.Contains(link.GameId))
                    .Select(link => link.GameId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }

            linkQuery = ApplySelectionFilters(linkQuery, filters, positionTarget);

            return await linkQuery
                .Select(link => link.GameId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        var draftQuery = dbContext.StagingGames
            .AsNoTracking()
            .Where(g => g.OwnerUserId == userId);

        if (explicitIds is { Count: > 0 })
        {
            return await draftQuery
                .Where(g => explicitIds.Contains(g.Id))
                .Select(g => g.Id)
                .ToListAsync(cancellationToken);
        }

        draftQuery = ApplySelectionFilters(draftQuery, filters, positionTarget);

        return await draftQuery
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<UserDatabaseGame> ApplySelectionFilters(
        IQueryable<UserDatabaseGame> query,
        GameSelectionFilters filters,
        PositionSearchTarget? positionTarget)
    {
        return query
            .ApplyPlayerFilters(
                filters.IgnoreColors,
                NormalizeNameToken(filters.WhiteFirstName),
                NormalizeNameToken(filters.WhiteLastName),
                NormalizeNameToken(filters.BlackFirstName),
                NormalizeNameToken(filters.BlackLastName))
            .ApplyScalarFilters(
                filters.EloEnabled,
                filters.EloFrom,
                filters.EloTo,
                filters.EloMode,
                filters.YearEnabled,
                filters.YearFrom,
                filters.YearTo,
                filters.EcoCode,
                filters.Result,
                filters.MoveCountFrom,
                filters.MoveCountTo)
            .ApplyPositionFilters(filters.SearchByPosition, positionTarget?.PosKey, filters.PositionMode, positionTarget?.PlyCount);
    }

    private static IQueryable<StagingGame> ApplySelectionFilters(
        IQueryable<StagingGame> query,
        GameSelectionFilters filters,
        PositionSearchTarget? positionTarget)
    {
        return query
            .ApplyPlayerFilters(
                filters.IgnoreColors,
                NormalizeNameToken(filters.WhiteFirstName),
                NormalizeNameToken(filters.WhiteLastName),
                NormalizeNameToken(filters.BlackFirstName),
                NormalizeNameToken(filters.BlackLastName))
            .ApplyScalarFilters(
                filters.EloEnabled,
                filters.EloFrom,
                filters.EloTo,
                filters.EloMode,
                filters.YearEnabled,
                filters.YearFrom,
                filters.YearTo,
                filters.EcoCode,
                filters.Result,
                filters.MoveCountFrom,
                filters.MoveCountTo)
            .ApplyPositionFilters(filters.SearchByPosition, positionTarget?.PosKey, filters.PositionMode, positionTarget?.PlyCount);
    }

    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpDelete("{id:guid}/games/{gameId:guid}")]
    public async Task<IActionResult> RemoveGame(Guid id, Guid gameId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var dbEntity = await dbContext.UserDatabases.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dbEntity is null)
        {
            return NotFound();
        }

        if (dbEntity.OwnerUserId != userId)
        {
            return Forbid();
        }

        var link = await dbContext.UserDatabaseGames
            .FirstOrDefaultAsync(x => x.UserDatabaseId == id && x.GameId == gameId, cancellationToken);

        if (link is null)
        {
            return NotFound();
        }

        dbContext.UserDatabaseGames.Remove(link);
        dbEntity.GameCount = Math.Max(0, dbEntity.GameCount - 1);
        dbEntity.ContentUpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Unlinks a set of games from this database in one request, and deletes any game that
    /// no longer belongs to any database at all.
    /// </summary>
    /// <remarks>
    /// Removing the link alone would leave the game and its (much larger) position rows
    /// behind with nothing referencing them - invisible to every user but still occupying
    /// storage. Deleting the newly-orphaned games mirrors what deleting a whole database
    /// already does, and keeps that one rule in both places.
    /// </remarks>
    [Authorize(Policy = ChessXivClaims.RegisteredUserPolicy)]
    [HttpPost("{id:guid}/games/remove")]
    public async Task<IActionResult> RemoveGames(
        Guid id,
        [FromBody] RemoveGamesFromDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.GameIds is null || request.GameIds.Count == 0)
        {
            return BadRequest("At least one game id is required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var dbEntity = await dbContext.UserDatabases.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (dbEntity is null)
        {
            return NotFound();
        }

        if (dbEntity.OwnerUserId != userId)
        {
            return Forbid();
        }

        var gameIds = request.GameIds.Where(g => g != Guid.Empty).Distinct().ToArray();
        if (gameIds.Length == 0)
        {
            return BadRequest("Provided game ids are invalid.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var removedCount = await dbContext.UserDatabaseGames
                .Where(link => link.UserDatabaseId == id && gameIds.Contains(link.GameId))
                .ExecuteDeleteAsync(cancellationToken);

            // Only games that lost their *last* link are deleted; one still shared with
            // another database must survive.
            var orphanIds = await dbContext.Games
                .AsNoTracking()
                .Where(game => gameIds.Contains(game.Id) && !game.UserDatabaseGames.Any())
                .Select(game => game.Id)
                .ToArrayAsync(cancellationToken);

            var deletedOrphanCount = 0;
            if (orphanIds.Length > 0)
            {
                deletedOrphanCount = await dbContext.Games
                    .Where(game => orphanIds.Contains(game.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await draftPromotionRepository.SyncGameCountAsync(id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Ok(new RemoveGamesFromDatabaseResponse(removedCount, deletedOrphanCount));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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
