using ChessXiv.Application.Services;

namespace ChessXiv.UnitTests;

public class PlayerNameNormalizerTests
{
    [Theory]
    [InlineData("CARLSEN", "carlsen")]
    [InlineData("  Garry   Kasparov\t", "garry kasparov")]
    [InlineData("José Raúl Capablanca", "jose raul capablanca")]
    [InlineData("Łasker", "lasker")]
    public void Normalize_HandlesCaseWhitespaceAndDiacritics(string input, string expected)
    {
        var actual = PlayerNameNormalizer.Normalize(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseNameParts_ParsesCommaSeparatedName()
    {
        var (firstName, lastName) = PlayerNameNormalizer.ParseNameParts("Kasparov, Garry");

        Assert.Equal("Garry", firstName);
        Assert.Equal("Kasparov", lastName);
    }

    [Fact]
    public void ParseNameParts_ParsesSpaceSeparatedName()
    {
        var (firstName, lastName) = PlayerNameNormalizer.ParseNameParts("Magnus Carlsen");

        Assert.Equal("Magnus", firstName);
        Assert.Equal("Carlsen", lastName);
    }

    [Fact]
    public void ParseNameParts_ParsesBotOrCompanyStyleNames()
    {
        var (firstName, lastName) = PlayerNameNormalizer.ParseNameParts("Deep Blue");

        Assert.Equal("Deep", firstName);
        Assert.Equal("Blue", lastName);
    }

    [Fact]
    public void Normalize_ProducesSameValue_ForAccentedAndUnaccentedVariants()
    {
        var accented = PlayerNameNormalizer.Normalize("José Raúl Capablanca");
        var unaccented = PlayerNameNormalizer.Normalize("Jose Raul Capablanca");

        Assert.Equal(unaccented, accented);
    }
}
