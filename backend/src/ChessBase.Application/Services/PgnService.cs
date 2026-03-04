using ChessBase.Application.Abstractions;
using ChessBase.Domain.Entities;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ChessBase.Application.Services;

public class PgnService : IPgnParser
{
    private static readonly Regex GameSeparatorRegex = new(@"(?:\r?\n[ \t]*){3,}", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"^\[(\w+)\s+""(.*)""\]$", RegexOptions.Compiled);
    private static readonly Regex WhiteMoveNumberRegex = new(@"^(\d+)\.$", RegexOptions.Compiled);
    private static readonly Regex BlackMoveNumberRegex = new(@"^(\d+)\.\.\.$", RegexOptions.Compiled);
    private static readonly Regex EvalRegex = new(@"%eval\s+([^\]\s]+)", RegexOptions.Compiled);
    private static readonly Regex ClockRegex = new(@"%clk\s+([^\]\s]+)", RegexOptions.Compiled);
    private static readonly HashSet<string> ResultTokens =
    [
        "1-0",
        "0-1",
        "1/2-1/2",
        "*"
    ];

    public IReadOnlyCollection<Game> ParsePgn(string pgn)
    {
        var games = new List<Game>();

        if (string.IsNullOrWhiteSpace(pgn))
        {
            return games;
        }

        var normalizedPgn = pgn.Replace("\r\n", "\n").Trim();
        var gameBlocks = GameSeparatorRegex
            .Split(normalizedPgn)
            .Where(block => !string.IsNullOrWhiteSpace(block));

        foreach (var gameBlock in gameBlocks)
        {
            var game = ParseSingleGame(gameBlock.Trim());
            games.Add(game);
        }

        return games;
    }

    private static Game ParseSingleGame(string gameBlock)
    {
        var game = new Game
        {
            Id = Guid.NewGuid(),
            White = string.Empty,
            Black = string.Empty,
            Result = "*",
            Pgn = gameBlock
        };

        var splitIndex = gameBlock.IndexOf("\n\n", StringComparison.Ordinal);
        var tagsPart = splitIndex >= 0 ? gameBlock[..splitIndex] : string.Empty;
        var movesPart = splitIndex >= 0 ? gameBlock[(splitIndex + 2)..].Trim() : gameBlock.Trim();
        
        var tags = ParseTags(tagsPart);
        ApplyTags(game, tags);

        if (ResultTokens.Contains(game.Result) && game.Result != "*")
        {
            movesPart = RemoveTrailingResultToken(movesPart, game.Result);
        }

        game.Moves = ParseMoves(movesPart, game.Id);

        if (game.Result == "*" && TryGetTrailingResult(movesPart, out var inferredResult))
        {
            game.Result = inferredResult;
        }

        return game;
    }

    private static Dictionary<string, string> ParseTags(string tagsPart)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tagsPart))
        {
            return tags;
        }

        var lines = tagsPart.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var match = TagRegex.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            var tagName = match.Groups[1].Value;
            var tagValue = match.Groups[2].Value;
            tags[tagName] = tagValue;
        }

        return tags;
    }

    private static void ApplyTags(Game game, IReadOnlyDictionary<string, string> tags)
    {
        game.Round = GetTag(tags, "Round");
        game.Event = GetTag(tags, "Event");
        game.Site = GetTag(tags, "Site");
        game.White = GetTag(tags, "White") ?? string.Empty;
        game.Black = GetTag(tags, "Black") ?? string.Empty;
        game.Result = GetTag(tags, "Result") ?? "*";
        game.TimeControl = GetTag(tags, "TimeControl");
        game.ECO = GetTag(tags, "ECO");
        game.Opening = GetTag(tags, "Opening");
        game.WhiteTitle = GetTag(tags, "WhiteTitle");
        game.BlackTitle = GetTag(tags, "BlackTitle");

        var whiteElo = GetTag(tags, "WhiteElo");
        if (int.TryParse(whiteElo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedWhiteElo))
        {
            game.WhiteElo = parsedWhiteElo;
        }

        var blackElo = GetTag(tags, "BlackElo");
        if (int.TryParse(blackElo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBlackElo))
        {
            game.BlackElo = parsedBlackElo;
        }

        var rawDate = GetTag(tags, "UTCDate") ?? GetTag(tags, "Date");
        if (TryParsePgnDate(rawDate, out var parsedDate))
        {
            game.Date = parsedDate;
        }
    }

    private static string? GetTag(IReadOnlyDictionary<string, string> tags, string tagName)
    {
        return tags.TryGetValue(tagName, out var value) ? value : null;
    }

    private static ICollection<Move> ParseMoves(string movesText, Guid gameId)
    {
        var moves = new List<Move>();
        if (string.IsNullOrWhiteSpace(movesText))
        {
            return moves;
        }

        var tokens = TokenizeMoves(movesText);
        var currentMoveNumber = 0;
        var expectingWhiteMove = true;
        Move? lastMove = null;
        Side? lastSide = null;

        foreach (var token in tokens)
        {
            if (token.IsComment)
            {
                if (lastMove is not null && lastSide.HasValue)
                {
                    ApplyMoveMetadata(lastMove, lastSide.Value, token.Value);
                }

                continue;
            }

            var value = token.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (WhiteMoveNumberRegex.IsMatch(value))
            {
                currentMoveNumber = int.Parse(WhiteMoveNumberRegex.Match(value).Groups[1].Value, CultureInfo.InvariantCulture);
                expectingWhiteMove = true;
                continue;
            }

            if (BlackMoveNumberRegex.IsMatch(value))
            {
                currentMoveNumber = int.Parse(BlackMoveNumberRegex.Match(value).Groups[1].Value, CultureInfo.InvariantCulture);
                expectingWhiteMove = false;
                continue;
            }

            if (ResultTokens.Contains(value))
            {
                continue;
            }

            if (value is "(" or ")")
            {
                continue;
            }

            var moveNumber = currentMoveNumber > 0 ? currentMoveNumber : moves.Count + 1;

            if (expectingWhiteMove)
            {
                var move = new Move
                {
                    Id = Guid.NewGuid(),
                    GameId = gameId,
                    MoveNumber = moveNumber,
                    WhiteMove = value
                };
                moves.Add(move);
                lastMove = move;
                lastSide = Side.White;
                expectingWhiteMove = false;
            }
            else
            {
                var move = moves.LastOrDefault(m => m.MoveNumber == moveNumber && string.IsNullOrWhiteSpace(m.BlackMove));
                if (move is null)
                {
                    move = new Move
                    {
                        Id = Guid.NewGuid(),
                        GameId = gameId,
                        MoveNumber = moveNumber,
                        WhiteMove = string.Empty
                    };
                    moves.Add(move);
                }

                move.BlackMove = value;
                lastMove = move;
                lastSide = Side.Black;
                expectingWhiteMove = true;
            }
        }

        return moves;
    }

    private static IEnumerable<Token> TokenizeMoves(string movesText)
    {
        var index = 0;
        while (index < movesText.Length)
        {
            if (char.IsWhiteSpace(movesText[index]))
            {
                index++;
                continue;
            }

            if (movesText[index] == '{')
            {
                var commentStart = ++index;
                var depth = 1;
                while (index < movesText.Length && depth > 0)
                {
                    if (movesText[index] == '{')
                    {
                        depth++;
                    }
                    else if (movesText[index] == '}')
                    {
                        depth--;
                    }

                    index++;
                }

                var commentLength = Math.Max(0, index - commentStart - 1);
                var comment = commentLength > 0
                    ? movesText.Substring(commentStart, commentLength)
                    : string.Empty;
                yield return new Token(comment, isComment: true);
                continue;
            }

            var tokenStart = index;
            while (index < movesText.Length && !char.IsWhiteSpace(movesText[index]) && movesText[index] != '{')
            {
                index++;
            }

            var token = movesText[tokenStart..index];
            yield return new Token(token, isComment: false);
        }
    }

    private static void ApplyMoveMetadata(Move move, Side side, string comment)
    {
        var evalMatch = EvalRegex.Match(comment);
        if (evalMatch.Success && TryParseEval(evalMatch.Groups[1].Value, out var eval))
        {
            if (side == Side.White)
            {
                move.WhiteEval = eval;
            }
            else
            {
                move.BlackEval = eval;
            }
        }

        var clkMatch = ClockRegex.Match(comment);
        if (clkMatch.Success)
        {
            if (side == Side.White)
            {
                move.WhiteClk = clkMatch.Groups[1].Value;
            }
            else
            {
                move.BlackClk = clkMatch.Groups[1].Value;
            }
        }
    }

    private static bool TryParseEval(string rawEval, out double eval)
    {
        eval = 0;
        if (string.IsNullOrWhiteSpace(rawEval))
        {
            return false;
        }

        if (rawEval.StartsWith('#'))
        {
            return false;
        }

        return double.TryParse(rawEval, NumberStyles.Float, CultureInfo.InvariantCulture, out eval);
    }

    private static bool TryParsePgnDate(string? rawDate, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(rawDate) || rawDate.Contains('?'))
        {
            return false;
        }

        return DateTime.TryParseExact(
            rawDate,
            "yyyy.MM.dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static bool TryGetTrailingResult(string movesText, out string result)
    {
        result = string.Empty;
        var tokens = movesText
            .Split((char[])null!, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return false;
        }

        var lastToken = tokens[^1];
        if (!ResultTokens.Contains(lastToken))
        {
            return false;
        }

        result = lastToken;
        return true;
    }

    private static string RemoveTrailingResultToken(string movesText, string result)
    {
        if (string.IsNullOrWhiteSpace(movesText) || string.IsNullOrWhiteSpace(result))
        {
            return movesText;
        }

        var trimmed = movesText.TrimEnd();
        if (!trimmed.EndsWith(result, StringComparison.Ordinal))
        {
            return movesText;
        }

        var withoutResult = trimmed[..^result.Length];
        return withoutResult.TrimEnd();
    }

    private enum Side
    {
        White,
        Black
    }

    private readonly struct Token(string value, bool isComment)
    {
        public string Value { get; } = value;
        public bool IsComment { get; } = isComment;
    }
}