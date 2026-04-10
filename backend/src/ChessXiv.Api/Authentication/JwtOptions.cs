namespace ChessXiv.Api.Authentication;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "ChessXiv.Api";
    public string Audience { get; set; } = "ChessXiv.Web";
    public string SigningKey { get; set; } = null!;
    public int ExpirationMinutes { get; set; } = 60;
}
