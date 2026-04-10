using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Services;

public sealed class DirectDatabaseImportService(
    IPgnParser pgnParser,
    IPositionImportCoordinator positionImportCoordinator,
    IGameRepository gameRepository,
    IUserDatabaseGameRepository userDatabaseGameRepository,
    IDraftPromotionRepository draftPromotionRepository,
    IUnitOfWork unitOfWork) : IDirectDatabaseImportService
{
    public async Task<DraftImportResult> ImportToDatabaseAsync(
        TextReader reader,
        string ownerUserId,
        Guid userDatabaseId,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        var userDatabase = await draftPromotionRepository.GetUserDatabaseAsync(userDatabaseId, cancellationToken);
        if (userDatabase is null)
        {
            throw new InvalidOperationException("Target user database was not found.");
        }

        if (!string.Equals(userDatabase.OwnerUserId, ownerUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Target user database does not belong to the current user.");
        }

        var parsedCount = 0;
        var importedCount = 0;
        var skippedCount = 0;
        var batch = new List<Game>(batchSize);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await foreach (var game in pgnParser.ParsePgnAsync(reader, cancellationToken))
            {
                parsedCount++;
                if (string.IsNullOrWhiteSpace(game.White) || string.IsNullOrWhiteSpace(game.Black))
                {
                    skippedCount++;
                    continue;
                }

                batch.Add(game);
                importedCount++;

                if (batch.Count >= batchSize)
                {
                    await PersistBatchAsync(batch, userDatabaseId, cancellationToken);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await PersistBatchAsync(batch, userDatabaseId, cancellationToken);
                batch.Clear();
            }

            await transaction.CommitAsync(cancellationToken);

            return new DraftImportResult(parsedCount, importedCount, skippedCount);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task PersistBatchAsync(IReadOnlyCollection<Game> games, Guid userDatabaseId, CancellationToken cancellationToken)
    {
        var addedAtUtc = DateTime.UtcNow;

        foreach (var game in games)
        {
            if (game.Date.HasValue)
            {
                game.Year = game.Date.Value.Year;
            }

            game.MoveCount = game.Moves.Count;
            ApplyNormalizedNames(game);
            game.GameHash = GameHashCalculator.Compute(game);
        }

        await positionImportCoordinator.PopulateAsync(games, cancellationToken);
        await gameRepository.AddRangeAsync(games, cancellationToken);

        var links = games.Select(g => new UserDatabaseGame
        {
            UserDatabaseId = userDatabaseId,
            GameId = g.Id,
            AddedAtUtc = addedAtUtc,
            Date = g.Date,
            Year = g.Year <= 0 ? null : g.Year,
            Event = g.Event,
            Round = g.Round,
            Site = g.Site
        }).ToArray();

        await userDatabaseGameRepository.AddRangeAsync(links, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        unitOfWork.ClearTracker();
    }

    private static void ApplyNormalizedNames(Game game)
    {
        ApplyNormalizedName(game.White, out var whiteFull, out var whiteFirst, out var whiteLast);
        ApplyNormalizedName(game.Black, out var blackFull, out var blackFirst, out var blackLast);

        game.WhiteNormalizedFullName = whiteFull;
        game.WhiteNormalizedFirstName = whiteFirst;
        game.WhiteNormalizedLastName = whiteLast;
        game.BlackNormalizedFullName = blackFull;
        game.BlackNormalizedFirstName = blackFirst;
        game.BlackNormalizedLastName = blackLast;
    }

    private static void ApplyNormalizedName(string rawName, out string full, out string? first, out string? last)
    {
        var (parsedFirst, parsedLast) = PlayerNameNormalizer.ParseNameParts(rawName);
        first = parsedFirst is null ? null : PlayerNameNormalizer.Normalize(parsedFirst);
        last = parsedLast is null ? null : PlayerNameNormalizer.Normalize(parsedLast);

        if (first is not null && last is not null)
        {
            full = PlayerNameNormalizer.Normalize($"{parsedFirst} {parsedLast}");
            return;
        }

        full = PlayerNameNormalizer.Normalize(rawName);
    }
}
