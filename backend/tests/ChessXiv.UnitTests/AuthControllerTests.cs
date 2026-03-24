using ChessXiv.Api.Authentication;
using ChessXiv.Api.Controllers;
using ChessXiv.Api.Email;
using ChessXiv.Api;
using ChessXiv.Application.Contracts;
using ChessXiv.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChessXiv.UnitTests;

public class AuthControllerTests
{
    [Fact]
    public async Task Register_ReturnsAccepted_AndSendsConfirmationEmail_WhenValidRequest()
    {
        var userManager = new TestUserManager
        {
            CreateAsyncHandler = (_, _) => Task.FromResult(IdentityResult.Success),
            GenerateEmailConfirmationTokenAsyncHandler = _ => Task.FromResult("email-confirm-token")
        };

        var emailSender = new FakeEmailSender();
        var controller = CreateController(userManager, new FakeJwtTokenService(), emailSender);

        var request = new AuthRegisterRequest("john", "john@example.com", "Password123");
        var actionResult = await controller.Register(request, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(actionResult);
        var response = Assert.IsType<AuthRegisterResponse>(accepted.Value);
        Assert.True(response.RequiresEmailConfirmation);
        Assert.Equal("john@example.com", response.Email);
        Assert.Equal("john@example.com", userManager.LastCreatedUser?.Email);
        Assert.Equal("john", userManager.LastCreatedUser?.UserName);
        Assert.Equal("Password123", userManager.LastCreatedPassword);
        Assert.Equal("john@example.com", emailSender.LastToEmail);
        Assert.Contains("Confirm your ChessXiv account", emailSender.LastSubject ?? string.Empty);
    }

    [Fact]
    public async Task Login_ReturnsForbidden_WhenEmailIsNotConfirmed()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "john",
            Email = "john@example.com",
            EmailConfirmed = false
        };

        var userManager = new TestUserManager
        {
            FindByNameAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            CheckPasswordAsyncHandler = (_, _) => Task.FromResult(true)
        };

        var controller = CreateController(userManager, new FakeJwtTokenService());
        var request = new AuthLoginRequest("john", "Password123");

        var actionResult = await controller.Login(request);

        var forbidden = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(403, forbidden.StatusCode);
    }

    [Fact]
    public async Task Register_ReturnsBadRequest_WhenUserAlreadyExists()
    {
        var userManager = new TestUserManager
        {
            CreateAsyncHandler = (_, _) => Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateUserName",
                Description = "Username already exists"
            }))
        };

        var controller = CreateController(userManager, new FakeJwtTokenService());

        var request = new AuthRegisterRequest("john", "john@example.com", "Password123");
        var actionResult = await controller.Register(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        Assert.Equal(400, badRequest.StatusCode ?? 400);
        var errors = ExtractErrors(badRequest.Value);
        Assert.Contains("Username already exists", errors);
    }

    [Fact]
    public async Task Register_ReturnsBadRequest_WhenEmailIsInvalid()
    {
        var userManager = new TestUserManager
        {
            CreateAsyncHandler = (_, _) => Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidEmail",
                Description = "Email 'not-an-email' is invalid."
            }))
        };

        var controller = CreateController(userManager, new FakeJwtTokenService());

        var request = new AuthRegisterRequest("john", "not-an-email", "Password123");
        var actionResult = await controller.Register(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var errors = ExtractErrors(badRequest.Value);
        Assert.Contains("Email 'not-an-email' is invalid.", errors);
    }

    [Fact]
    public async Task Register_ReturnsBadRequest_WhenPasswordIsWeak()
    {
        var userManager = new TestUserManager
        {
            CreateAsyncHandler = (_, _) => Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequiresNonAlphanumeric",
                Description = "Passwords must have at least one non alphanumeric character."
            }))
        };

        var controller = CreateController(userManager, new FakeJwtTokenService());

        var request = new AuthRegisterRequest("john", "john@example.com", "Password123");
        var actionResult = await controller.Register(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var errors = ExtractErrors(badRequest.Value);
        Assert.Contains("Passwords must have at least one non alphanumeric character.", errors);
    }

    [Fact]
    public async Task Login_ReturnsOkWithAuthToken_WhenPasswordIsCorrect()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "john",
            Email = "john@example.com",
            EmailConfirmed = true
        };

        var userManager = new TestUserManager
        {
            FindByNameAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            CheckPasswordAsyncHandler = (_, _) => Task.FromResult(true)
        };

        var tokenService = new FakeJwtTokenService
        {
            AccessToken = "login-token"
        };

        var controller = CreateController(userManager, tokenService);

        var request = new AuthLoginRequest("john", "Password123");
        var actionResult = await controller.Login(request);

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<AuthTokenResponse>(ok.Value);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.Equal("login-token", response.AccessToken);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenUserNotFound()
    {
        var userManager = new TestUserManager
        {
            FindByNameAsyncHandler = _ => Task.FromResult<ApplicationUser?>(null),
            FindByEmailAsyncHandler = _ => Task.FromResult<ApplicationUser?>(null)
        };

        var controller = CreateController(userManager, new FakeJwtTokenService());

        var request = new AuthLoginRequest("missing-user", "Password123");
        var actionResult = await controller.Login(request);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(actionResult);
        Assert.Equal("Invalid credentials.", unauthorized.Value);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenPasswordIsWrong()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "john",
            Email = "john@example.com"
        };

        var userManager = new TestUserManager
        {
            FindByNameAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            FindByEmailAsyncHandler = _ => Task.FromResult<ApplicationUser?>(null),
            CheckPasswordAsyncHandler = (_, _) => Task.FromResult(false)
        };

        var controller = CreateController(userManager, new FakeJwtTokenService());

        var request = new AuthLoginRequest("john", "wrong-password");
        var actionResult = await controller.Login(request);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(actionResult);
        Assert.Equal("Invalid credentials.", unauthorized.Value);
    }

    [Fact]
    public async Task ChangePendingEmail_ReturnsOk_AndSendsConfirmation_WhenCredentialsValid()
    {
        var user = new ApplicationUser
        {
            Id = "user-2",
            UserName = "pending-user",
            Email = "old@example.com",
            EmailConfirmed = false
        };

        var userManager = new TestUserManager
        {
            FindByNameAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            CheckPasswordAsyncHandler = (_, _) => Task.FromResult(true),
            SetEmailAsyncHandler = (_, _) => Task.FromResult(IdentityResult.Success),
            GenerateEmailConfirmationTokenAsyncHandler = _ => Task.FromResult("changed-email-confirm-token")
        };

        var emailSender = new FakeEmailSender();
        var controller = CreateController(userManager, new FakeJwtTokenService(), emailSender);

        var request = new ChangePendingEmailRequest("pending-user", "Password123", "new@example.com");
        var actionResult = await controller.ChangePendingEmail(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult);
        Assert.Equal("Email address updated. Please confirm your email address before signing in.", ok.Value);
        Assert.Equal("new@example.com", userManager.LastSetEmailValue);
        Assert.Equal("new@example.com", emailSender.LastToEmail);
    }

    private sealed class FakeJwtTokenService : IJwtTokenService
    {
        public string AccessToken { get; init; } = "token";

        public AuthTokenResponse CreateToken(ApplicationUser user)
        {
            return new AuthTokenResponse(AccessToken, DateTime.UtcNow.AddMinutes(60));
        }
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public string? LastToEmail { get; private set; }
        public string? LastSubject { get; private set; }
        public string? LastBody { get; private set; }

        public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
        {
            LastToEmail = toEmail;
            LastSubject = subject;
            LastBody = body;
            return Task.CompletedTask;
        }
    }

    private sealed class TestUserManager : UserManager<ApplicationUser>
    {
        public Func<ApplicationUser, string, Task<IdentityResult>>? CreateAsyncHandler { get; init; }
        public Func<string, Task<ApplicationUser?>>? FindByNameAsyncHandler { get; init; }
        public Func<string, Task<ApplicationUser?>>? FindByEmailAsyncHandler { get; init; }
        public Func<ApplicationUser, string, Task<bool>>? CheckPasswordAsyncHandler { get; init; }
        public Func<ApplicationUser, string, Task<IdentityResult>>? SetEmailAsyncHandler { get; init; }
        public Func<ApplicationUser, Task<string>>? GenerateEmailConfirmationTokenAsyncHandler { get; init; }
        public ApplicationUser? LastCreatedUser { get; private set; }
        public string? LastCreatedPassword { get; private set; }
        public string? LastSetEmailValue { get; private set; }

        public TestUserManager()
            : base(
                new InMemoryUserStore(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new EmptyServiceProvider(),
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
        }

        public override Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
        {
            LastCreatedUser = user;
            LastCreatedPassword = password;
            return CreateAsyncHandler?.Invoke(user, password)
                ?? Task.FromResult(IdentityResult.Success);
        }

        public override Task<ApplicationUser?> FindByNameAsync(string userName)
        {
            return FindByNameAsyncHandler?.Invoke(userName)
                ?? Task.FromResult<ApplicationUser?>(null);
        }

        public override Task<ApplicationUser?> FindByEmailAsync(string email)
        {
            return FindByEmailAsyncHandler?.Invoke(email)
                ?? Task.FromResult<ApplicationUser?>(null);
        }

        public override Task<bool> CheckPasswordAsync(ApplicationUser user, string password)
        {
            return CheckPasswordAsyncHandler?.Invoke(user, password)
                ?? Task.FromResult(false);
        }

        public override Task<IdentityResult> SetEmailAsync(ApplicationUser user, string? email)
        {
            LastSetEmailValue = email;

            if (email is not null)
            {
                user.Email = email;
            }

            return SetEmailAsyncHandler?.Invoke(user, email ?? string.Empty)
                ?? Task.FromResult(IdentityResult.Success);
        }

        public override Task<string> GenerateEmailConfirmationTokenAsync(ApplicationUser user)
        {
            return GenerateEmailConfirmationTokenAsyncHandler?.Invoke(user)
                ?? Task.FromResult("confirmation-token");
        }
    }

    private sealed class InMemoryUserStore : IUserStore<ApplicationUser>
    {
        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.Id);
        }

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.UserName);
        }

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(user.NormalizedUserName);
        }

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            return Task.FromResult<ApplicationUser?>(null);
        }

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            return Task.FromResult<ApplicationUser?>(null);
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private static string[] ExtractErrors(object? badRequestValue)
    {
        Assert.NotNull(badRequestValue);
        var errorsProperty = badRequestValue!.GetType().GetProperty("Errors");
        Assert.NotNull(errorsProperty);

        var errors = errorsProperty!.GetValue(badRequestValue) as string[];
        Assert.NotNull(errors);
        return errors!;
    }

    private static AuthController CreateController(
        TestUserManager userManager,
        FakeJwtTokenService tokenService,
        FakeEmailSender? emailSender = null)
    {
        return new AuthController(
            userManager,
            tokenService,
            emailSender ?? new FakeEmailSender(),
            Options.Create(new FrontendOptions { BaseUrl = "https://chessxiv.org" }));
    }
}
