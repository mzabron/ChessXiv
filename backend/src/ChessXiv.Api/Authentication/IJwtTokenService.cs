using ChessXiv.Application.Contracts;
using ChessXiv.Infrastructure.Data;

namespace ChessXiv.Api.Authentication;

public interface IJwtTokenService
{
    AuthTokenResponse CreateToken(ApplicationUser user);

    /// <summary>
    /// Issues a throwaway token for an anonymous visitor so that guests can upload and
    /// explore a PGN without an account. Guest tokens carry <see cref="ChessXivClaims.Guest"/>,
    /// which the RegisteredUser policy rejects, so they can never save anything.
    /// </summary>
    AuthTokenResponse CreateGuestToken();

    /// <summary>
    /// Validates a previously issued guest token entirely out of band from the normal
    /// Authorization-header pipeline, and returns its subject if it is a genuine, unexpired
    /// guest token. Used to migrate an anonymous guest's draft onto the account they just
    /// signed into: the caller is authenticated as the NEW user, so the OLD guest token has
    /// to travel as a request value instead of a header.
    /// </summary>
    string? TryGetGuestUserId(string guestToken);
}
