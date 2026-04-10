using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Services;

public class PgnImportService(
	IPgnParser pgnParser,
	IGameRepository gameRepository,
	IPositionImportCoordinator positionImportCoordinator,
	IUnitOfWork unitOfWork) : IPgnImportService
{
	public async Task<PgnImportResult> ImportAsync(TextReader reader, bool markAsMaster = false, int batchSize = 500, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(reader);

		if (batchSize <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
		}

		var parsedCount = 0;
		var importedCount = 0;
		var skippedCount = 0;
		var batch = new List<Game>(batchSize);

		await foreach (var game in pgnParser.ParsePgnAsync(reader, cancellationToken))
		{
			parsedCount++;
			if (string.IsNullOrWhiteSpace(game.White) || string.IsNullOrWhiteSpace(game.Black))
			{
				skippedCount++;
				continue;
			}

			game.IsMaster = markAsMaster;
			batch.Add(game);
			importedCount++;

			if (batch.Count >= batchSize)
			{
				await PersistBatchAsync(batch, cancellationToken);
				batch.Clear();
			}
		}

		if (batch.Count > 0)
		{
			await PersistBatchAsync(batch, cancellationToken);
		}

		return new PgnImportResult(
			ParsedCount: parsedCount,
			ImportedCount: importedCount,
			SkippedCount: skippedCount);
	}

	private async Task PersistBatchAsync(IReadOnlyCollection<Domain.Entities.Game> games, CancellationToken cancellationToken)
	{
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
