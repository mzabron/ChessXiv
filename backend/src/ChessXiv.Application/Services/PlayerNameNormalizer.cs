using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ChessXiv.Application.Services;

internal static partial class PlayerNameNormalizer
{
    private static readonly Regex MultiSpaceRegex = MultiSpaceRegexFactory();

    public static string Normalize(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var normalized = trimmed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lowered = char.ToLowerInvariant(ch);
            builder.Append(Transliterate(lowered));
        }

        var compact = MultiSpaceRegex.Replace(builder.ToString(), " ").Trim();
        return compact;
    }

    public static (string? FirstName, string? LastName) ParseNameParts(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (null, null);
        }

        var normalized = fullName.Trim();
        if (normalized.Contains(','))
        {
            var parts = normalized.Split(',', 2, StringSplitOptions.TrimEntries);
            var last = parts[0];
            var first = parts.Length > 1 ? parts[1] : null;
            return (string.IsNullOrWhiteSpace(first) ? null : first, string.IsNullOrWhiteSpace(last) ? null : last);
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1)
        {
            return (tokens[0], null);
        }

        var firstName = tokens[0];
        var lastName = tokens[^1];
        return (firstName, lastName);
    }

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex MultiSpaceRegexFactory();

    private static char Transliterate(char ch)
    {
        return ch switch
        {
            'ł' => 'l',
            'đ' => 'd',
            'ð' => 'd',
            _ => ch
        };
    }
}
