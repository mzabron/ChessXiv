using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ChessXiv.Api.Authentication;
using ChessXiv.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace ChessXiv.UnitTests;

public class JwtTokenServiceTests
{
    [Fact]
    public void CreateToken_ContainsUserIdClaim_AndExpectedIssuerAndExpiration()
    {
        var now = DateTime.UtcNow;
        var options = Options.Create(new JwtOptions
        {
            Issuer = "ChessXiv.Api",
            Audience = "ChessXiv.Web",
            SigningKey = "very-long-test-signing-key-for-jwt-123456789",
            ExpirationMinutes = 60
        });

        var service = new JwtTokenService(options);
        var user = new ApplicationUser
        {
            Id = "user-123",
            UserName = "john",
            Email = "john@example.com"
        };

        var result = service.CreateToken(user);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        var subClaim = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub);
        var nameIdClaim = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);

        Assert.NotNull(subClaim);
        Assert.NotNull(nameIdClaim);
        Assert.Equal(user.Id, subClaim!.Value);
        Assert.Equal(user.Id, nameIdClaim!.Value);
        Assert.True(token.ValidTo > now.AddMinutes(55));
        Assert.Equal("ChessXiv.Api", token.Issuer);
    }
}
