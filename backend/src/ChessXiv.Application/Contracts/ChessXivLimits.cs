namespace ChessXiv.Application.Contracts;

/// <summary>
/// The only limits in the product. There are no user tiers and no per-tier quotas:
/// every signed-in account gets the same allowance, and the CLI importer bypasses
/// these entirely because it does not go through the HTTP API.
/// </summary>
public static class ChessXivLimits
{
    /// <summary>
    /// Largest PGN accepted by an upload endpoint. Kept at 100 MB because Cloudflare
    /// terminates larger request bodies before they ever reach the origin.
    /// </summary>
    public const long MaxUploadBytes = 100L * 1024 * 1024;

    /// <summary>Distinct games one account may keep across all of its saved databases.</summary>
    public const int MaxSavedGamesPerUser = 10_000;
}
