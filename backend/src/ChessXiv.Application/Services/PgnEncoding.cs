using System.Text;

namespace ChessXiv.Application.Services;

/// <summary>
/// Chooses the text encoding a PGN file should be read with.
///
/// Both import paths used to open PGNs with a plain <c>new StreamReader(stream)</c>, which
/// means UTF-8. A great many PGN files are not UTF-8 - exports from ChessBase and similar
/// desktop tools are usually a single-byte Windows code page - and every accented player
/// name in such a file decodes to U+FFFD. Nothing fails; the names are simply wrong in the
/// database, and wrong in a way that no later query can undo.
/// </summary>
public static class PgnEncoding
{
    /// <summary>
    /// Enough to cover the header tags of several thousand games, which is where non-ASCII
    /// characters live in practice. Detection cannot read the whole of a multi-gigabyte
    /// file without doubling the I/O of the import, so this is the trade - callers that
    /// know better can pass an explicit encoding instead.
    /// </summary>
    private const int SampleBytes = 1024 * 1024;

    static PgnEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// The assumption when a file is definitely not UTF-8. Windows-1252 rather than
    /// Latin-1: they agree on the accented letters and differ only over 0x80-0x9F, where
    /// Windows-1252 has the typographic characters those exports actually contain.
    /// </summary>
    /// <remarks>
    /// Lazy, and deliberately so: a static field initializer runs <i>before</i> the static
    /// constructor body, so resolving this eagerly asked for code page 1252 before the
    /// provider that supplies it had been registered - and silently settled for Latin-1,
    /// which differs from 1252 over 0x80-0x9F.
    /// </remarks>
    public static Encoding LegacyFallback => LazyLegacyFallback.Value;

    private static readonly Lazy<Encoding> LazyLegacyFallback = new(() => ResolveOrLatin1(1252));

    /// <summary>
    /// Resolves a user-supplied encoding name, e.g. "utf-8", "windows-1250", "iso-8859-2".
    /// </summary>
    public static bool TryResolve(string? name, out Encoding encoding)
    {
        encoding = Encoding.UTF8;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            encoding = Encoding.GetEncoding(name.Trim());
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens a reader over <paramref name="stream"/>. When <paramref name="forced"/> is null
    /// the encoding is detected; a byte-order mark always wins over either.
    /// </summary>
    public static StreamReader OpenReader(Stream stream, Encoding? forced, out Encoding chosen)
    {
        ArgumentNullException.ThrowIfNull(stream);

        chosen = forced ?? Detect(stream);

        // A 64 KB buffer rather than the 1 KB default: these files are measured in
        // gigabytes, where the syscall count of the default buffer is pure overhead.
        return new StreamReader(
            stream,
            chosen,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: true);
    }

    /// <summary>
    /// UTF-8 if the start of the stream decodes cleanly as UTF-8, the legacy fallback if it
    /// does not. Pure ASCII is valid UTF-8, so plain files take the UTF-8 branch and both
    /// answers would have been identical anyway. The stream is rewound before returning.
    /// </summary>
    public static Encoding Detect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            return Encoding.UTF8;
        }

        var origin = stream.Position;
        try
        {
            var buffer = new byte[SampleBytes];
            var read = ReadAtLeast(stream, buffer);
            return IsValidUtf8(buffer.AsSpan(0, read)) ? Encoding.UTF8 : LegacyFallback;
        }
        finally
        {
            stream.Position = origin;
        }
    }

    private static int ReadAtLeast(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> sample)
    {
        // The sample almost certainly cuts a multi-byte character in half, and a truncated
        // sequence is not evidence that the file is not UTF-8. Drop a trailing partial one.
        var end = sample.Length;
        for (var step = 0; step < 4 && end > 0; step++)
        {
            var b = sample[end - 1];
            if ((b & 0x80) == 0x00)
            {
                break;
            }

            end--;

            // A lead byte ends the walk: everything from here on was the truncated part.
            if ((b & 0xC0) != 0x80)
            {
                break;
            }
        }

        try
        {
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(sample[..end]);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static Encoding ResolveOrLatin1(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Latin-1 is always present and agrees with 1252 on the accented letters.
            return Encoding.Latin1;
        }
    }
}
