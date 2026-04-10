using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ChessXiv.Domain.Entities;

namespace ChessXiv.Application.Services;

public static class GameHashCalculator
{
    private static readonly Regex UciRegex = new("^[a-h][1-8][a-h][1-8][nbrq]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AnnotationRegex = new("[+#?!]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Compute(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var normalizedWhite = NormalizePlayerNameForHash(game.White);
        var normalizedBlack = NormalizePlayerNameForHash(game.Black);
        var normalizedMoves = NormalizeMoves(game.Moves);

        var payload = $"{normalizedWhite}|{normalizedBlack}|{normalizedMoves}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizePlayerNameForHash(string rawName)
    {
        var (firstName, lastName) = PlayerNameNormalizer.ParseNameParts(rawName);
        if (!string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName))
        {
            return PlayerNameNormalizer.Normalize($"{firstName} {lastName}");
        }

        return PlayerNameNormalizer.Normalize(rawName);
    }

    private static string NormalizeMoves(IEnumerable<Move> moves)
    {
        var ordered = moves.OrderBy(m => m.MoveNumber).ToArray();
        var parts = new List<string>(ordered.Length * 2);

        foreach (var move in ordered)
        {
            if (!string.IsNullOrWhiteSpace(move.WhiteMove))
            {
                var normalized = NormalizeMoveToken(move.WhiteMove);
                if (normalized.Length > 0)
                {
                    parts.Add(normalized);
                }
            }

            if (!string.IsNullOrWhiteSpace(move.BlackMove))
            {
                var normalized = NormalizeMoveToken(move.BlackMove!);
                if (normalized.Length > 0)
                {
                    parts.Add(normalized);
                }
            }
        }

        return string.Join(' ', parts);
    }

    private static string NormalizeMoveToken(string token)
    {
        var normalized = token.Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = normalized.Replace("0-0-0", "O-O-O", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("0-0", "O-O", StringComparison.OrdinalIgnoreCase);

        var lower = normalized.ToLowerInvariant();
        if (UciRegex.IsMatch(lower))
        {
            return NormalizeUciToComparableToken(lower);
        }

        var withoutAnnotations = AnnotationRegex.Replace(normalized, string.Empty);
        withoutAnnotations = withoutAnnotations.Replace("e.p.", string.Empty, StringComparison.OrdinalIgnoreCase);
        withoutAnnotations = withoutAnnotations.Replace("ep", string.Empty, StringComparison.OrdinalIgnoreCase);
        withoutAnnotations = withoutAnnotations.Replace("=", string.Empty, StringComparison.Ordinal);

        return withoutAnnotations.Trim().ToLowerInvariant();
    }

    private static string NormalizeUciToComparableToken(string uci)
    {
        var fromFile = uci[0];
        var fromRank = uci[1];
        var toFile = uci[2];
        var toRank = uci[3];
        var promotion = uci.Length == 5 ? uci[4].ToString(CultureInfo.InvariantCulture) : string.Empty;

        var isLikelyPawn = fromFile == toFile;
        if (isLikelyPawn)
        {
            return $"{toFile}{toRank}{promotion}";
        }

        return $"{fromFile}{fromRank}{toFile}{toRank}{promotion}";
    }
}
