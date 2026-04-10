using ChessXiv.Application.Contracts;
using ChessXiv.Infrastructure.Data;

namespace ChessXiv.Api.Authentication;

public interface IJwtTokenService
{
    AuthTokenResponse CreateToken(ApplicationUser user);
}
