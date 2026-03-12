using ChessBase.Application.Abstractions;
using ChessBase.Application.Abstractions.Repositories;
using ChessBase.Application.Contracts;
using ChessBase.Domain.Entities;

namespace ChessBase.Application.Services;

public class PgnImportService(
	IPgnParser pgnParser,
	IGameRepository gameRepository,
	IPlayerRepository playerRepository,
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
		}

		await ResolvePlayersAsync(games, cancellationToken);
		await positionImportCoordinator.PopulateAsync(games, cancellationToken);
		await gameRepository.AddRangeAsync(games, cancellationToken);
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}

	private async Task ResolvePlayersAsync(IReadOnlyCollection<Game> games, CancellationToken cancellationToken)
	{
		var normalizedToOriginalName = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var name in games.SelectMany(g => new[] { g.White, g.Black }))
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			var normalizedName = PlayerNameNormalizer.Normalize(name);
			if (normalizedName.Length == 0 || normalizedToOriginalName.ContainsKey(normalizedName))
			{
				continue;
			}

			normalizedToOriginalName[normalizedName] = name;
		}

		var normalizedNames = normalizedToOriginalName.Keys.ToArray();

		if (normalizedNames.Length == 0)
		{
			return;
		}

		var existingPlayers = await playerRepository.GetByNormalizedFullNamesAsync(normalizedNames, cancellationToken);
		var playersByNormalizedName = new Dictionary<string, Player>(existingPlayers, StringComparer.Ordinal);
		var missingPlayers = new List<Player>();

		foreach (var normalizedName in normalizedNames)
		{
			if (playersByNormalizedName.ContainsKey(normalizedName))
			{
				continue;
			}

			var fullName = normalizedToOriginalName[normalizedName];

			var (firstName, lastName) = PlayerNameNormalizer.ParseNameParts(fullName);

			var player = new Player
			{
				Id = Guid.NewGuid(),
				FullName = fullName,
				NormalizedFullName = normalizedName,
				FirstName = firstName,
				LastName = lastName,
				NormalizedFirstName = firstName is null ? null : PlayerNameNormalizer.Normalize(firstName),
				NormalizedLastName = lastName is null ? null : PlayerNameNormalizer.Normalize(lastName)
			};

			missingPlayers.Add(player);
		}

		if (missingPlayers.Count > 0)
		{
			await playerRepository.AddRangeAsync(missingPlayers, cancellationToken);
			foreach (var player in missingPlayers)
			{
				playersByNormalizedName[player.NormalizedFullName] = player;
			}
		}

		foreach (var game in games)
		{
			var whiteNormalizedName = PlayerNameNormalizer.Normalize(game.White);
			var blackNormalizedName = PlayerNameNormalizer.Normalize(game.Black);

			if (playersByNormalizedName.TryGetValue(whiteNormalizedName, out var whitePlayer))
			{
				game.WhitePlayerId = whitePlayer.Id;
			}

			if (playersByNormalizedName.TryGetValue(blackNormalizedName, out var blackPlayer))
			{
				game.BlackPlayerId = blackPlayer.Id;
			}
		}
	}
}
