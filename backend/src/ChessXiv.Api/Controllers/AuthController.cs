using System.Text;
using ChessXiv.Api.Authentication;
using ChessXiv.Api.Email;
using ChessXiv.Application.Contracts;
using ChessXiv.Infrastructure.Data;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace ChessXiv.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    IJwtTokenService jwtTokenService,
    IEmailSender emailSender,
    IOptions<FrontendOptions> frontendOptions) : ControllerBase
{
    private readonly FrontendOptions _frontendOptions = frontendOptions.Value;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] AuthRegisterRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Login, email and password are required.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Login.Trim(),
            Email = request.Email.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(new
            {
                Errors = createResult.Errors.Select(e => e.Description).ToArray()
            });
        }

        await SendEmailConfirmationAsync(user, cancellationToken);

        return Accepted(new AuthRegisterResponse(
            RequiresEmailConfirmation: true,
            Email: user.Email ?? request.Email.Trim(),
            Message: "Registration created. Please confirm your email to sign in."));
    }

    [HttpPost("login")]
    [EnableRateLimiting("AuthLogin")]
    public async Task<IActionResult> Login([FromBody] AuthLoginRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Login and password are required.");
        }

        var login = request.Login.Trim();
        var user = await userManager.FindByNameAsync(login) ?? await userManager.FindByEmailAsync(login);

        if (user is null)
        {
            return Unauthorized("Invalid credentials.");
        }

        var isPasswordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            return Unauthorized("Invalid credentials.");
        }

        if (!user.EmailConfirmed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Code = "EMAIL_NOT_CONFIRMED",
                Message = "Email confirmation is required before signing in.",
                Email = user.Email
            });
        }

        var token = jwtTokenService.CreateToken(user);
        return Ok(token);
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest("User id and token are required.");
        }

        var user = await userManager.FindByIdAsync(request.UserId.Trim());
        if (user is null)
        {
            return BadRequest("Invalid email confirmation request.");
        }

        if (user.EmailConfirmed)
        {
            var alreadyConfirmedToken = jwtTokenService.CreateToken(user);
            return Ok(alreadyConfirmedToken);
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

        var confirmResult = await userManager.ConfirmEmailAsync(user, decodedToken);
        if (!confirmResult.Succeeded)
        {
            return BadRequest(new
            {
                Errors = confirmResult.Errors.Select(e => e.Description).ToArray()
            });
        }

        var token = jwtTokenService.CreateToken(user);
        return Ok(token);
    }

    [HttpPost("resend-confirmation")]
    [EnableRateLimiting("AuthForgotPassword")]
    public async Task<IActionResult> ResendConfirmation([FromBody] ResendEmailConfirmationRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.UsernameOrEmail))
        {
            return BadRequest("Username or email is required.");
        }

        var identifier = request.UsernameOrEmail.Trim();
        var user = await userManager.FindByNameAsync(identifier) ?? await userManager.FindByEmailAsync(identifier);

        if (user is not null && !user.EmailConfirmed)
        {
            await SendEmailConfirmationAsync(user, cancellationToken);
        }

        return Ok("If the account exists and is not confirmed, a confirmation email has been sent.");
    }

    [HttpPost("change-pending-email")]
    [EnableRateLimiting("AuthForgotPassword")]
    public async Task<IActionResult> ChangePendingEmail([FromBody] ChangePendingEmailRequest request, CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.UsernameOrEmail)
            || string.IsNullOrWhiteSpace(request.Password)
            || string.IsNullOrWhiteSpace(request.NewEmail))
        {
            return BadRequest("Username/email, password and new email are required.");
        }

        var identifier = request.UsernameOrEmail.Trim();
        var newEmail = request.NewEmail.Trim();
        var user = await userManager.FindByNameAsync(identifier) ?? await userManager.FindByEmailAsync(identifier);

        if (user is null || user.EmailConfirmed)
        {
            return BadRequest("Only unconfirmed accounts can change email address.");
        }

        var isPasswordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            return Unauthorized("Invalid credentials.");
        }

        if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Please enter a different email address.");
        }

        user.EmailConfirmed = false;
        var setEmailResult = await userManager.SetEmailAsync(user, newEmail);
        if (!setEmailResult.Succeeded)
        {
            return BadRequest(new
            {
                Errors = setEmailResult.Errors.Select(e => e.Description).ToArray()
            });
        }

        await SendEmailConfirmationAsync(user, cancellationToken);
        return Ok("Email address updated. Please confirm your email address before signing in.");
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("AuthForgotPassword")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("Email is required.");
        }

        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);

        if (user is not null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var frontendBaseUrl = _frontendOptions.BaseUrl.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(frontendBaseUrl))
            {
                throw new InvalidOperationException("Frontend:BaseUrl configuration is required for password reset links.");
            }

            var resetUrl = $"{frontendBaseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(encodedToken)}";

            await emailSender.SendAsync(
                email,
                "ChessXiv password reset",
                $"<p>You requested a password reset for ChessXiv.</p><p><a href=\"{resetUrl}\">Reset your password</a></p><p>If you did not request this, you can safely ignore this email.</p>",
                cancellationToken);
        }

        return Ok("If the email exists, password reset instructions have been sent.");
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest("Email, token and new password are required.");
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return BadRequest("Invalid password reset request.");
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

        var resetResult = await userManager.ResetPasswordAsync(user, decodedToken, request.NewPassword);
        if (!resetResult.Succeeded)
        {
            return BadRequest(new
            {
                Errors = resetResult.Errors.Select(e => e.Description).ToArray()
            });
        }

        return Ok("Password has been reset.");
    }

    private async Task SendEmailConfirmationAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var frontendBaseUrl = _frontendOptions.BaseUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(frontendBaseUrl))
        {
            throw new InvalidOperationException("Frontend:BaseUrl configuration is required for email confirmation links.");
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("Cannot send email confirmation because the user email is missing.");
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var confirmationUrl = $"{frontendBaseUrl}/confirm-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(encodedToken)}";

        await emailSender.SendAsync(
            user.Email,
            "Confirm your ChessXiv account",
            $"<p>Welcome to ChessXiv.</p><p><a href=\"{confirmationUrl}\">Confirm your email</a></p><p>If you did not create this account, you can ignore this message.</p>",
            cancellationToken);
    }
}
