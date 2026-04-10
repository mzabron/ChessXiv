using System.Security.Claims;
using System.Text;
using ChessXiv.Api.Email;
using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChessXiv.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController(
    UserManager<ApplicationUser> userManager,
    ChessXivDbContext dbContext,
    IQuotaService quotaService,
    IEmailSender emailSender,
    IOptions<FrontendOptions> frontendOptions) : ControllerBase
{
    private readonly FrontendOptions _frontendOptions = frontendOptions.Value;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Unauthorized();
        }

        var savedGamesUsed = await dbContext.UserDatabaseGames
            .AsNoTracking()
            .Where(x => x.UserDatabase.OwnerUserId == userId)
            .Select(x => x.GameId)
            .Distinct()
            .CountAsync(cancellationToken);

        var importedGamesUsed = await dbContext.StagingGames
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .CountAsync(cancellationToken);

        var importedGamesLimit = await quotaService.GetMaxDraftImportGamesAsync(userId, cancellationToken);
        var savedGamesLimit = await quotaService.GetMaxSavedGamesAsync(userId, cancellationToken);

        var response = new AccountSummaryResponse(
            Nickname: user.UserName ?? string.Empty,
            Email: user.Email ?? string.Empty,
            SavedGamesUsed: savedGamesUsed,
            SavedGamesLimit: savedGamesLimit,
            ImportedGamesUsed: importedGamesUsed,
            ImportedGamesLimit: importedGamesLimit);

        return Ok(response);
    }

    [HttpPost("change-email")]
    public async Task<IActionResult> ChangeEmail([FromBody] ChangeAccountEmailRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.NewEmail) || string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return BadRequest("New email and current password are required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Unauthorized();
        }

        var isPasswordValid = await userManager.CheckPasswordAsync(user, request.CurrentPassword);
        if (!isPasswordValid)
        {
            return Unauthorized("Current password is invalid.");
        }

        var normalizedEmail = request.NewEmail.Trim();
        if (string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Please enter a different email address.");
        }

        var existingUser = await userManager.FindByEmailAsync(normalizedEmail);
        if (existingUser is not null && !string.Equals(existingUser.Id, user.Id, StringComparison.Ordinal))
        {
            return BadRequest("Email is already in use.");
        }

        await SendEmailChangeConfirmationAsync(user, normalizedEmail, cancellationToken);
        return Ok("Check your email inbox to confirm address change.");
    }

    [AllowAnonymous]
    [HttpPost("confirm-email-change")]
    public async Task<IActionResult> ConfirmEmailChange([FromBody] ConfirmAccountEmailChangeRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.NewEmail) || string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest("User id, new email and token are required.");
        }

        var user = await userManager.FindByIdAsync(request.UserId.Trim());
        if (user is null)
        {
            return BadRequest("Invalid email change request.");
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token.Trim()));
        }
        catch (FormatException)
        {
            return BadRequest("Invalid token format.");
        }

        var normalizedEmail = request.NewEmail.Trim();

        if (string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Ok("Email address changed and confirmed.");
        }

        var result = await userManager.ChangeEmailAsync(user, normalizedEmail, decodedToken);
        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                Errors = result.Errors.Select(e => e.Description).ToArray()
            });
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return BadRequest(new
                {
                    Errors = updateResult.Errors.Select(e => e.Description).ToArray()
                });
            }
        }

        return Ok("Email address changed and confirmed.");
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangeAccountPasswordRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest("Current password and new password are required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                Errors = result.Errors.Select(e => e.Description).ToArray()
            });
        }

        return Ok("Password updated successfully.");
    }

    [HttpPost("delete")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Password is required.");
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Unauthorized();
        }

        var isPasswordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            return Unauthorized("Invalid credentials.");
        }

        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            return BadRequest(new
            {
                Errors = deleteResult.Errors.Select(e => e.Description).ToArray()
            });
        }

        return Ok("Account deleted.");
    }

    private string? GetCurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
    }

    private async Task SendEmailChangeConfirmationAsync(ApplicationUser user, string newEmail, CancellationToken cancellationToken)
    {
        var frontendBaseUrl = _frontendOptions.BaseUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(frontendBaseUrl))
        {
            throw new InvalidOperationException("Frontend:BaseUrl configuration is required for email confirmation links.");
        }

        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var confirmationUrl = $"{frontendBaseUrl}/confirm-email-change?userId={Uri.EscapeDataString(user.Id)}&newEmail={Uri.EscapeDataString(newEmail)}&token={Uri.EscapeDataString(encodedToken)}";

        await emailSender.SendAsync(
            newEmail,
            "Confirm your new ChessXiv email",
            $"<p>You have requested a change to your email address.</p><p>To confirm that you have access to this email address, please use the link below:</p><p><a href=\"{confirmationUrl}\">{confirmationUrl}</a></p><p>Link disabled? Try pasting it into your browser's address bar.</p>",
            cancellationToken);
    }
}
