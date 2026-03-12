using ChessBase.Application.Services;

namespace ChessBase.UnitTests;

public class PgnServiceTagTests
{
    private readonly PgnService _service = new();

    [Fact]
    public void ParsePgn_MapsExpectedTags_FromSampleFile()
    {
        var pgn = PgnServiceTestData.LoadGamesSamplePgn();

        var games = _service.ParsePgn(pgn).ToList();
        var first = games.First();

        Assert.NotEmpty(games);
        Assert.Equal("Round 7: Neagu, Roberto Florin - Lianes Garcia, Marcos", first.Event);
        Assert.Equal("7.9", first.Round);
        Assert.Equal("Neagu, Roberto Florin", first.White);
        Assert.Equal("Lianes Garcia, Marcos", first.Black);
        Assert.Equal("1-0", first.Result);
        Assert.Equal(2196, first.WhiteElo);
        Assert.Equal(2457, first.BlackElo);
        Assert.Equal("FM", first.WhiteTitle);
        Assert.Equal("IM", first.BlackTitle);
        Assert.Equal("10+2", first.TimeControl);
        Assert.Equal("A04", first.ECO);
        Assert.Equal("Zukertort Opening: Kingside Fianchetto", first.Opening);
        Assert.Equal(new DateTime(2026, 1, 4), first.Date);
    }

    [Fact]
    public void ParsePgn_ParsesMultipleGames_FromSampleFile()
    {
        var pgn = PgnServiceTestData.LoadGamesSamplePgn();

        var games = _service.ParsePgn(pgn).ToList();

        Assert.True(games.Count >= 4);
        Assert.Contains(games, game => game.Result == "1/2-1/2");
        Assert.Contains(games, game => game.Result == "0-1");
        Assert.All(games, game => Assert.False(string.IsNullOrWhiteSpace(game.White)));
        Assert.All(games, game => Assert.False(string.IsNullOrWhiteSpace(game.Black)));
    }

    [Fact]
    public void ParsePgn_UsesDateTag_WhenUtcDateIsMissing()
    {
        var pgn = PgnServiceTestData.LoadGamesSamplePgn();
        var withoutUtcDate = pgn.Replace("[UTCDate \"2026.01.04\"]\n", string.Empty, StringComparison.Ordinal);

        var game = _service.ParsePgn(withoutUtcDate).First();

        Assert.Equal(new DateTime(2026, 1, 4), game.Date);
    }

    [Fact]
    public void ParsePgn_ExtractsYear_WhenDateIsPartial()
    {
        const string pgn = """
            [Event "Year Fallback"]
            [Site "Test"]
            [Date "2023.??.??"]
            [White "Alpha"]
            [Black "Beta"]
            [Result "1-0"]

            1. e4 e5 1-0
            """;

        var game = _service.ParsePgn(pgn).Single();

        Assert.Null(game.Date);
        Assert.Equal(2023, game.Year);
    }
}
