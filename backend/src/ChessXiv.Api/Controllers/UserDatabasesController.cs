using System.Security.Claims;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChessXiv.Api.Controllers;

[ApiController]
[Route("api/user-databases")]
public class UserDatabasesController(ChessXivDbContext dbContext) : ControllerBase
{
    private const string DefaultStartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Authorize]
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var items = await dbContext.UserDatabases
            .AsNoTracking()
            .Where(d => d.OwnerUserId == userId)
            .OrderBy(d => d.Name)
            .Select(d => new UserDatabaseDto(
                d.Id,
                d.Name,
                d.IsPublic,
                d.OwnerUserId,
                d.UserDatabaseGames.Count,
                d.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [Authorize]
    [HttpGet("bookmarks")]
    public async Task<IActionResult> GetBookmarks(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var items = await dbContext.UserDatabaseBookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .Where(b => b.UserDatabase.IsPublic || b.UserDatabase.OwnerUserId == userId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new BookmarkedUserDatabaseDto(
                b.UserDatabase.Id,
                b.UserDatabase.Name,
                b.UserDatabase.IsPublic,
                b.UserDatabase.OwnerUserId,
                b.UserDatabase.UserDatabaseGames.Count,
                b.UserDatabase.CreatedAtUtc,
                b.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(items);
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
                d.UserDatabaseGames.Count,
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

        var dbInfo = await dbContext.UserDatabases
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new
            {
                d.Id,
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

        var normalizedSortBy = (sortBy ?? "createdAt").Trim().ToLowerInvariant();
        var normalizedSortDirection = (sortDirection ?? "desc").Trim().ToLowerInvariant();
        var normalizedResultSortMode = (resultSortMode ?? "default").Trim().ToLowerInvariant();
        var descending = normalizedSortDirection != "asc";

        var query = dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(link => link.UserDatabaseId == id)
            .Select(link => new
            {
                Link = link,
                Game = link.Game
            });

        query = (normalizedSortBy, descending) switch
        {
            ("year", true) => query
                .OrderBy(x => x.Game.Year <= 0 ? 1 : 0)
                .ThenByDescending(x => x.Game.Year)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("year", false) => query
                .OrderBy(x => x.Game.Year <= 0 ? 1 : 0)
                .ThenBy(x => x.Game.Year)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("white", true) => query.OrderByDescending(x => x.Game.White).ThenByDescending(x => x.Link.AddedAtUtc),
            ("white", false) => query.OrderBy(x => x.Game.White).ThenByDescending(x => x.Link.AddedAtUtc),
            ("black", true) => query.OrderByDescending(x => x.Game.Black).ThenByDescending(x => x.Link.AddedAtUtc),
            ("black", false) => query.OrderBy(x => x.Game.Black).ThenByDescending(x => x.Link.AddedAtUtc),
            ("whiteelo", true) => query
                .OrderBy(x => x.Game.WhiteElo == null ? 1 : 0)
                .ThenByDescending(x => x.Game.WhiteElo)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("whiteelo", false) => query
                .OrderBy(x => x.Game.WhiteElo == null ? 1 : 0)
                .ThenBy(x => x.Game.WhiteElo)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("blackelo", true) => query
                .OrderBy(x => x.Game.BlackElo == null ? 1 : 0)
                .ThenByDescending(x => x.Game.BlackElo)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("blackelo", false) => query
                .OrderBy(x => x.Game.BlackElo == null ? 1 : 0)
                .ThenBy(x => x.Game.BlackElo)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("result", _) when normalizedResultSortMode == "whitefirst" => query
                .OrderBy(x => x.Game.Result == "1-0" ? 0 : x.Game.Result == "0-1" ? 1 : x.Game.Result == "1/2-1/2" ? 2 : 3)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("result", _) when normalizedResultSortMode == "blackfirst" => query
                .OrderBy(x => x.Game.Result == "0-1" ? 0 : x.Game.Result == "1-0" ? 1 : x.Game.Result == "1/2-1/2" ? 2 : 3)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("result", _) when normalizedResultSortMode == "drawfirst" => query
                .OrderBy(x => x.Game.Result == "1/2-1/2" ? 0 : x.Game.Result == "1-0" ? 1 : x.Game.Result == "0-1" ? 2 : 3)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("result", _) => query.OrderByDescending(x => x.Link.AddedAtUtc).ThenByDescending(x => x.Game.Id),
            ("eco", true) => query
                .OrderBy(x => x.Game.ECO == null || x.Game.ECO == "" || x.Game.ECO == "?" ? 1 : 0)
                .ThenByDescending(x => x.Game.ECO)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("eco", false) => query
                .OrderBy(x => x.Game.ECO == null || x.Game.ECO == "" || x.Game.ECO == "?" ? 1 : 0)
                .ThenBy(x => x.Game.ECO)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("event", true) => query
                .OrderBy(x => x.Game.Event == null || x.Game.Event == "" || x.Game.Event == "?" || x.Game.Event == "-" ? 1 : 0)
                .ThenByDescending(x => x.Game.Event)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("event", false) => query
                .OrderBy(x => x.Game.Event == null || x.Game.Event == "" || x.Game.Event == "?" || x.Game.Event == "-" ? 1 : 0)
                .ThenBy(x => x.Game.Event)
                .ThenByDescending(x => x.Link.AddedAtUtc),
            ("moves", true) => query.OrderByDescending(x => x.Game.MoveCount).ThenByDescending(x => x.Link.AddedAtUtc),
            ("moves", false) => query.OrderBy(x => x.Game.MoveCount).ThenByDescending(x => x.Link.AddedAtUtc),
            (_, false) => query.OrderBy(x => x.Link.AddedAtUtc).ThenBy(x => x.Game.Id),
            _ => query.OrderByDescending(x => x.Link.AddedAtUtc).ThenByDescending(x => x.Game.Id)
        };

        var totalCount = await query.CountAsync(cancellationToken);

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
                x.Link.AddedAtUtc))
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

        var moves = await dbContext.Moves
            .AsNoTracking()
            .Where(m => m.GameId == gameId)
            .OrderBy(m => m.MoveNumber)
            .Select(m => new GameReplayMoveDto(
                m.MoveNumber,
                m.WhiteMove,
                m.BlackMove,
                string.IsNullOrWhiteSpace(m.WhiteClk) ? null : m.WhiteClk,
                string.IsNullOrWhiteSpace(m.BlackClk) ? null : m.BlackClk))
            .ToListAsync(cancellationToken);

        var fenHistory = await dbContext.Positions
            .AsNoTracking()
            .Where(p => p.GameId == gameId)
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

        var entity = new UserDatabase
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            IsPublic = request.IsPublic,
            OwnerUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.UserDatabases.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = new UserDatabaseDto(entity.Id, entity.Name, entity.IsPublic, entity.OwnerUserId, 0, entity.CreatedAtUtc);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, dto);
    }

    [Authorize]
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

    [Authorize]
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

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var linkedGameIds = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(x => x.UserDatabaseId == id)
            .Select(x => x.GameId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        await dbContext.UserDatabaseGames
            .Where(x => x.UserDatabaseId == id)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.UserDatabases
            .Where(d => d.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

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
                    .ToArrayAsync(cancellationToken);

                if (orphanIds.Length == 0)
                {
                    continue;
                }

                await dbContext.Games
                    .Where(g => orphanIds.Contains(g.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return NoContent();
    }

    [Authorize]
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

    [Authorize]
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

    [Authorize]
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
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            AddedCount = toInsert.Length,
            SkippedCount = alreadyLinked.Count
        });
    }

    [Authorize]
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
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
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
}
