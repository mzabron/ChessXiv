using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Services;

/// <summary>
/// Fills in the derived fields every import path needs before a parsed game can be stored:
/// year, move count, normalised player names and the dedup hash. Previously this block was
/// copy-pasted into each of the import services and the CLI.
/// </summary>
public static class ParsedGameFinalizer
{
    public static Game Finalize(ParsedGame parsedGame)
    {
        var game = parsedGame.Game;

        if (game.Date.HasValue)
        {
            game.Year = game.Date.Value.Year;
        }

        game.MoveCount = parsedGame.Moves.Count;
        ApplyNormalizedNames(game);
        game.GameHash = GameHashCalculator.Compute(game, parsedGame.Moves);

        return game;
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
