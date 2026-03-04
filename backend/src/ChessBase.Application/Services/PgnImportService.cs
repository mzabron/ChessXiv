using ChessBase.Application.Abstractions;
using ChessBase.Application.Abstractions.Repositories;
using ChessBase.Application.Contracts;

namespace ChessBase.Application.Services;

public class PgnImportService(IPgnParser pgnParser, IGameRepository gameRepository, IUnitOfWork unitOfWork) : IPgnImportService
{
	public async Task<PgnImportResult> ImportAsync(string pgn, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(pgn))
		{
			return new PgnImportResult(ParsedCount: 0, ImportedCount: 0, SkippedCount: 0);
		}

		var parsedGames = pgnParser.ParsePgn(pgn);
		if (parsedGames.Count == 0)
		{
			return new PgnImportResult(ParsedCount: 0, ImportedCount: 0, SkippedCount: 0);
		}

		var validGames = parsedGames
			.Where(game => !string.IsNullOrWhiteSpace(game.White) && !string.IsNullOrWhiteSpace(game.Black))
			.ToList();

		if (validGames.Count > 0)
		{
			await gameRepository.AddRangeAsync(validGames, cancellationToken);
			await unitOfWork.SaveChangesAsync(cancellationToken);
		}

		return new PgnImportResult(
			ParsedCount: parsedGames.Count,
			ImportedCount: validGames.Count,
			SkippedCount: parsedGames.Count - validGames.Count);
	}
}
