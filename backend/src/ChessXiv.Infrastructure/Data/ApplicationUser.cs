using Microsoft.AspNetCore.Identity;

namespace ChessXiv.Infrastructure.Data;

public class ApplicationUser : IdentityUser
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string UserTier { get; set; } = "Free";
}
