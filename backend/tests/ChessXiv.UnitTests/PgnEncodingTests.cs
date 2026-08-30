using System.Text;
using ChessXiv.Application.Services;

namespace ChessXiv.UnitTests;

public class PgnEncodingTests
{
    [Fact]
    public void LegacyFallback_IsWindows1252_NotLatin1()
    {
        // Not a pedantic assertion. The two agree on the accented letters, so every other
        // test here passed while this silently resolved to Latin-1 - which decodes
        // 0x80-0x9F, the range holding the curly quotes and dashes that Windows exports are
        // full of, as control characters instead.
        Assert.Equal(1252, PgnEncoding.LegacyFallback.CodePage);
    }

    [Fact]
    public void LegacyFallback_DecodesTheWindows1252SpecificRange()
    {
        // 0x93/0x94 are curly double quotes in windows-1252 and controls in Latin-1.
        var decoded = PgnEncoding.LegacyFallback.GetString([0x93, 0x41, 0x94]);

        Assert.Equal("\u201cA\u201d", decoded);
    }

    [Fact]
    public void Detect_ReturnsUtf8_ForPlainAscii()
    {
        using var stream = new MemoryStream("[White \"Smith, John\"]"u8.ToArray());

        Assert.Equal(Encoding.UTF8, PgnEncoding.Detect(stream));
    }

    [Fact]
    public void Detect_ReturnsUtf8_ForGenuineUtf8AccentedNames()
    {
        var bytes = Encoding.UTF8.GetBytes("[White \"Ząbroń, Maksym\"]\n[Black \"Réti, Richard\"]");
        using var stream = new MemoryStream(bytes);

        Assert.Equal(Encoding.UTF8, PgnEncoding.Detect(stream));
    }

    [Fact]
    public void Detect_ReturnsLegacyFallback_ForSingleByteAccentedNames()
    {
        // The exact shape of a ChessBase-style export: one byte per accented letter, which
        // is not valid UTF-8 and used to decode to U+FFFD.
        var bytes = PgnEncoding.LegacyFallback.GetBytes("[White \"Réti, Richard\"]");
        using var stream = new MemoryStream(bytes);

        Assert.Equal(PgnEncoding.LegacyFallback, PgnEncoding.Detect(stream));
    }

    [Fact]
    public void Detect_RewindsTheStream()
    {
        using var stream = new MemoryStream("[Event \"Test\"]"u8.ToArray());
        stream.Position = 3;

        PgnEncoding.Detect(stream);

        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void Detect_IsNotFooledByAMultiByteCharacterCutOffByTheSampleBoundary()
    {
        // A UTF-8 file long enough to exceed the detection sample, arranged so the sample
        // ends part-way through a two-byte character. A truncated sequence is not evidence
        // that the file is not UTF-8, and treating it as such would mis-detect the whole
        // file - and then mangle every accented name in it.
        var padding = new string('a', 1024 * 1024 - 1);
        var bytes = Encoding.UTF8.GetBytes(padding + "ń" + new string('b', 64));
        using var stream = new MemoryStream(bytes);

        Assert.Equal(Encoding.UTF8, PgnEncoding.Detect(stream));
    }

    [Fact]
    public void OpenReader_LetsAByteOrderMarkOverrideTheDetectedEncoding()
    {
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("[White \"Réti\"]"))
            .ToArray();
        using var stream = new MemoryStream(bytes);

        using var reader = PgnEncoding.OpenReader(stream, forced: PgnEncoding.LegacyFallback, out _);
        var text = reader.ReadToEnd();

        Assert.Equal("[White \"Réti\"]", text);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("windows-1250")]
    [InlineData("windows-1252")]
    [InlineData("iso-8859-2")]
    public void TryResolve_AcceptsTheEncodingNamesTheCliDocuments(string name)
    {
        Assert.True(PgnEncoding.TryResolve(name, out _));
    }

    [Theory]
    [InlineData("not-an-encoding")]
    [InlineData("")]
    [InlineData(null)]
    public void TryResolve_RejectsUnknownNames(string? name)
    {
        Assert.False(PgnEncoding.TryResolve(name, out _));
    }
}
