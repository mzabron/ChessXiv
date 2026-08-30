using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChessXiv.Application.Contracts;
using ChessXiv.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ChessXiv.Api.Authentication;

public class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private const int GuestTokenLifetimeHours = 12;

    private readonly JwtOptions _options = options.Value;

    public AuthTokenResponse CreateGuestToken()
    {
        var guestId = $"{ChessXivClaims.GuestUserIdPrefix}{Guid.NewGuid():N}";

        return CreateToken(
            [
                new Claim(JwtRegisteredClaimNames.Sub, guestId),
                new Claim(ClaimTypes.NameIdentifier, guestId),
                new Claim(JwtRegisteredClaimNames.UniqueName, "guest"),
                new Claim(ChessXivClaims.Guest, "true")
            ],
            TimeSpan.FromHours(GuestTokenLifetimeHours));
    }

    public AuthTokenResponse CreateToken(ApplicationUser user)
    {
        return CreateToken(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty)
            ],
            TimeSpan.FromMinutes(_options.ExpirationMinutes));
    }

    public string? TryGetGuestUserId(string guestToken)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var handler = new JwtSecurityTokenHandler();

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(guestToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ValidIssuer = _options.Issuer,
                ValidAudience = _options.Audience,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Malformed token string.
            return null;
        }

        var isGuest = principal.Claims.Any(c => c.Type == ChessXivClaims.Guest && c.Value == "true");
        if (!isGuest)
        {
            return null;
        }

        return principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private AuthTokenResponse CreateToken(IReadOnlyCollection<Claim> claims, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAtUtc = DateTime.UtcNow.Add(lifetime);

        var tokenDescriptor = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        return new AuthTokenResponse(accessToken, expiresAtUtc);
    }
}
