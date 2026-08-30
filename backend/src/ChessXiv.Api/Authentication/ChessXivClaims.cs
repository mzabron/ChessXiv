namespace ChessXiv.Api.Authentication;

public static class ChessXivClaims
{
    /// <summary>Present and "true" only on anonymous guest-session tokens.</summary>
    public const string Guest = "chessxiv:guest";

    /// <summary>
    /// Prefix on the subject of a guest token. Staging rows are keyed by the subject, so
    /// this keeps guest drafts in the same table as registered users' drafts while still
    /// making them trivially distinguishable for cleanup and for save checks.
    /// </summary>
    public const string GuestUserIdPrefix = "guest:";

    /// <summary>Authorization policy that admits signed-in accounts but not guests.</summary>
    public const string RegisteredUserPolicy = "RegisteredUser";
}
