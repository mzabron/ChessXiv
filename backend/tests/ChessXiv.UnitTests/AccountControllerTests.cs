using ChessXiv.Api;
using ChessXiv.Api.Controllers;
using ChessXiv.Api.Email;
using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Contracts;
using ChessXiv.Domain.Entities;
using ChessXiv.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace ChessXiv.UnitTests;

public class AccountControllerTests
{
    [Fact]
    public async Task GetSummary_ReturnsOk_WithNicknameEmailAndQuotaUsage()
    {
        await using var dbContext = CreateInMemoryDbContext();

        var user = new ApplicationUser
        {
            Id = "summary-user",
            UserName = "johnny",
            Email = "johnny@example.com"
        };

        var database = new UserDatabase
        {
            Id = Guid.NewGuid(),
            OwnerUserId = user.Id,
            Name = "my-db",
            IsPublic = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        var gameA = new Domain.Entities.Game
        {
            Id = Guid.NewGuid(),
            White = "Alpha",
            Black = "Beta",
            Result = "*",
            Pgn = "1. e4 e5 *",
            MoveCount = 1,
            GameHash = "hash-a"
        };

        var gameB = new Domain.Entities.Game
        {
            Id = Guid.NewGuid(),
            White = "Gamma",
            Black = "Delta",
            Result = "*",
            Pgn = "1. d4 d5 *",
            MoveCount = 1,
            GameHash = "hash-b"
        };

        dbContext.Users.Add(user);
        dbContext.UserDatabases.Add(database);
        dbContext.Games.AddRange(gameA, gameB);
        dbContext.UserDatabaseGames.AddRange(
            new UserDatabaseGame
            {
                UserDatabaseId = database.Id,
                GameId = gameA.Id,
                AddedAtUtc = DateTime.UtcNow
            },
            new UserDatabaseGame
            {
                UserDatabaseId = database.Id,
                GameId = gameB.Id,
                AddedAtUtc = DateTime.UtcNow
            });
        dbContext.StagingGames.AddRange(
            new StagingGame
            {
                Id = Guid.NewGuid(),
                OwnerUserId = user.Id,
                CreatedAtUtc = DateTime.UtcNow,
                White = "Alpha",
                Black = "Beta",
                Result = "*",
                Pgn = "1. e4 e5 *",
                MoveCount = 1,
                GameHash = "staging-hash-1"
            },
            new StagingGame
            {
                Id = Guid.NewGuid(),
                OwnerUserId = user.Id,
                CreatedAtUtc = DateTime.UtcNow,
                White = "Alpha",
                Black = "Beta",
                Result = "*",
                Pgn = "1. e4 e5 *",
                MoveCount = 1,
                GameHash = "staging-hash-2"
            });

        await dbContext.SaveChangesAsync();

        var userManager = new TestUserManager
        {
            FindByIdAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user)
        };

        var controller = CreateController(userManager, new FakeEmailSender(), dbContext);
        SetControllerUserId(controller, user.Id);

        var result = await controller.GetSummary(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var summary = Assert.IsType<AccountSummaryResponse>(ok.Value);
        Assert.Equal("johnny", summary.Nickname);
        Assert.Equal("johnny@example.com", summary.Email);
        Assert.Equal(2, summary.SavedGamesUsed);
        Assert.Equal(10_000, summary.SavedGamesLimit);
        Assert.Equal(2, summary.ImportedGamesUsed);
        Assert.Equal(200_000, summary.ImportedGamesLimit);
    }

    [Fact]
    public async Task ChangeEmail_Success_ReturnsOk_GeneratesToken_AndSendsEmail()
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
            FindByIdAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            CheckPasswordAsyncHandler = (_, _) => Task.FromResult(true),
            FindByEmailAsyncHandler = _ => Task.FromResult<ApplicationUser?>(null),
            GenerateChangeEmailTokenAsyncHandler = (_, _) => Task.FromResult("change-email-token")
        };

        var emailSender = new FakeEmailSender();
        var controller = CreateController(userManager, emailSender);
        SetControllerUserId(controller, user.Id);

        var request = new ChangeAccountEmailRequest("new@example.com", "Password123");
        var result = await controller.ChangeEmail(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Check your email inbox to confirm address change.", ok.Value);
        Assert.Equal("new@example.com", userManager.LastGeneratedChangeEmailTarget);
        Assert.Equal("new@example.com", emailSender.LastToEmail);
        Assert.Contains("confirm-email-change", emailSender.LastBody ?? string.Empty, StringComparison.Ordinal);

        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("change-email-token"));
        Assert.Contains(encodedToken, emailSender.LastBody ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeEmail_WrongPassword_ReturnsUnauthorized()
    {
        var user = new ApplicationUser
        {
            Id = "user-2",
            UserName = "john",
            Email = "john@example.com"
        };

        var userManager = new TestUserManager
        {
            FindByIdAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            CheckPasswordAsyncHandler = (_, _) => Task.FromResult(false)
        };

        var controller = CreateController(userManager, new FakeEmailSender());
        SetControllerUserId(controller, user.Id);

        var request = new ChangeAccountEmailRequest("new@example.com", "wrong-password");
        var result = await controller.ChangeEmail(request, CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Current password is invalid.", unauthorized.Value);
    }

    [Fact]
    public async Task ChangeEmail_DuplicateEmail_ReturnsBadRequest()
    {
        var user = new ApplicationUser
        {
            Id = "user-3",
            UserName = "john",
            Email = "john@example.com"
        };

        var duplicateUser = new ApplicationUser
        {
            Id = "other-user",
            UserName = "other",
            Email = "new@example.com"
        };

        var userManager = new TestUserManager
        {
            FindByIdAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            CheckPasswordAsyncHandler = (_, _) => Task.FromResult(true),
            FindByEmailAsyncHandler = _ => Task.FromResult<ApplicationUser?>(duplicateUser)
        };

        var controller = CreateController(userManager, new FakeEmailSender());
        SetControllerUserId(controller, user.Id);

        var request = new ChangeAccountEmailRequest("new@example.com", "Password123");
        var result = await controller.ChangeEmail(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Email is already in use.", badRequest.Value);
    }

    [Fact]
    public async Task ConfirmEmailChange_HappyPath_DecodesToken_ChangesEmail_AndConfirms()
    {
        var user = new ApplicationUser
        {
            Id = "user-4",
            UserName = "john",
            Email = "old@example.com",
            EmailConfirmed = false
        };

        var userManager = new TestUserManager
        {
            FindByIdAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            ChangeEmailAsyncHandler = (_, _, _) => Task.FromResult(IdentityResult.Success),
            UpdateAsyncHandler = _ => Task.FromResult(IdentityResult.Success)
        };

        var controller = CreateController(userManager, new FakeEmailSender());
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("change-token"));

        var request = new ConfirmAccountEmailChangeRequest(user.Id, "new@example.com", encodedToken);
        var result = await controller.ConfirmEmailChange(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Email address changed and confirmed.", ok.Value);
        Assert.Equal("new@example.com", userManager.LastChangedEmail);
        Assert.Equal("change-token", userManager.LastChangeEmailToken);
        Assert.True(user.EmailConfirmed);
        Assert.True(userManager.WasUpdateCalled);
    }

    [Fact]
    public async Task ConfirmEmailChange_InvalidToken_ReturnsBadRequest()
    {
        var user = new ApplicationUser
        {
            Id = "user-5",
            UserName = "john",
            Email = "old@example.com",
            EmailConfirmed = false
        };

        var userManager = new TestUserManager
        {
            FindByIdAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            ChangeEmailAsyncHandler = (_, _, _) => Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidToken",
                Description = "Invalid token."
            }))
        };

        var controller = CreateController(userManager, new FakeEmailSender());
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("invalid-token"));

        var request = new ConfirmAccountEmailChangeRequest(user.Id, "new@example.com", encodedToken);
        var result = await controller.ConfirmEmailChange(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var errors = ExtractErrors(badRequest.Value);
        Assert.Contains("Invalid token.", errors);
    }

    [Fact]
    public async Task ChangePassword_Success_ReturnsOk()
    {
        var user = new ApplicationUser
        {
            Id = "user-6",
            UserName = "john",
            Email = "john@example.com"
        };

        var userManager = new TestUserManager
        {
            FindByIdAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            ChangePasswordAsyncHandler = (_, _, _) => Task.FromResult(IdentityResult.Success)
        };

        var controller = CreateController(userManager, new FakeEmailSender());
        SetControllerUserId(controller, user.Id);

        var request = new ChangeAccountPasswordRequest("Current123", "NewPassword123");
        var result = await controller.ChangePassword(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Password updated successfully.", ok.Value);
    }

    [Fact]
    public async Task ChangePassword_Failure_ReturnsBadRequest()
    {
        var user = new ApplicationUser
        {
            Id = "user-7",
            UserName = "john",
            Email = "john@example.com"
        };

        var userManager = new TestUserManager
        {
            FindByIdAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            ChangePasswordAsyncHandler = (_, _, _) => Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordTooShort",
                Description = "Passwords must be at least 8 characters."
            }))
        };

        var controller = CreateController(userManager, new FakeEmailSender());
        SetControllerUserId(controller, user.Id);

        var request = new ChangeAccountPasswordRequest("Current123", "short");
        var result = await controller.ChangePassword(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var errors = ExtractErrors(badRequest.Value);
        Assert.Contains("Passwords must be at least 8 characters.", errors);
    }

    [Fact]
    public async Task DeleteAccount_Success_ReturnsOk()
    {
        var user = new ApplicationUser
        {
            Id = "user-8",
            UserName = "john",
            Email = "john@example.com"
        };

        var userManager = new TestUserManager
        {
            FindByIdAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            CheckPasswordAsyncHandler = (_, _) => Task.FromResult(true),
            DeleteAsyncHandler = _ => Task.FromResult(IdentityResult.Success)
        };

        var controller = CreateController(userManager, new FakeEmailSender());
        SetControllerUserId(controller, user.Id);

        var request = new DeleteAccountRequest("Password123");
        var result = await controller.DeleteAccount(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Account deleted.", ok.Value);
        Assert.Equal(user.Id, userManager.LastDeletedUserId);
    }

    [Fact]
    public async Task DeleteAccount_WrongPassword_ReturnsUnauthorized()
    {
        var user = new ApplicationUser
        {
            Id = "user-9",
            UserName = "john",
            Email = "john@example.com"
        };

        var userManager = new TestUserManager
        {
            FindByIdAsyncHandler = _ => Task.FromResult<ApplicationUser?>(user),
            CheckPasswordAsyncHandler = (_, _) => Task.FromResult(false)
        };

        var controller = CreateController(userManager, new FakeEmailSender());
        SetControllerUserId(controller, user.Id);

        var request = new DeleteAccountRequest("WrongPassword");
        var result = await controller.DeleteAccount(request);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Invalid credentials.", unauthorized.Value);
    }

    private sealed class StubQuotaService : IQuotaService
    {
        public Task<int> GetMaxDraftImportGamesAsync(string? ownerUserId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(200_000);
        }

        public Task<int> GetMaxSavedGamesAsync(string? ownerUserId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(10_000);
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
        public Func<string, Task<ApplicationUser?>>? FindByIdAsyncHandler { get; init; }
        public Func<string, Task<ApplicationUser?>>? FindByEmailAsyncHandler { get; init; }
        public Func<ApplicationUser, string, Task<bool>>? CheckPasswordAsyncHandler { get; init; }
        public Func<ApplicationUser, string, Task<string>>? GenerateChangeEmailTokenAsyncHandler { get; init; }
        public Func<ApplicationUser, string, string, Task<IdentityResult>>? ChangeEmailAsyncHandler { get; init; }
        public Func<ApplicationUser, Task<IdentityResult>>? UpdateAsyncHandler { get; init; }
        public Func<ApplicationUser, string, string, Task<IdentityResult>>? ChangePasswordAsyncHandler { get; init; }
        public Func<ApplicationUser, Task<IdentityResult>>? DeleteAsyncHandler { get; init; }

        public string? LastGeneratedChangeEmailTarget { get; private set; }
        public string? LastChangedEmail { get; private set; }
        public string? LastChangeEmailToken { get; private set; }
        public bool WasUpdateCalled { get; private set; }
        public string? LastDeletedUserId { get; private set; }

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

        public override Task<ApplicationUser?> FindByIdAsync(string userId)
        {
            return FindByIdAsyncHandler?.Invoke(userId)
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

        public override Task<string> GenerateChangeEmailTokenAsync(ApplicationUser user, string newEmail)
        {
            LastGeneratedChangeEmailTarget = newEmail;
            return GenerateChangeEmailTokenAsyncHandler?.Invoke(user, newEmail)
                ?? Task.FromResult("change-email-token");
        }

        public override Task<IdentityResult> ChangeEmailAsync(ApplicationUser user, string newEmail, string token)
        {
            LastChangedEmail = newEmail;
            LastChangeEmailToken = token;

            user.Email = newEmail;

            return ChangeEmailAsyncHandler?.Invoke(user, newEmail, token)
                ?? Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> UpdateAsync(ApplicationUser user)
        {
            WasUpdateCalled = true;
            return UpdateAsyncHandler?.Invoke(user)
                ?? Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> ChangePasswordAsync(ApplicationUser user, string currentPassword, string newPassword)
        {
            return ChangePasswordAsyncHandler?.Invoke(user, currentPassword, newPassword)
                ?? Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> DeleteAsync(ApplicationUser user)
        {
            LastDeletedUserId = user.Id;
            return DeleteAsyncHandler?.Invoke(user)
                ?? Task.FromResult(IdentityResult.Success);
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

    private static void SetControllerUserId(ControllerBase controller, string userId)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, userId)
        };

        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Test");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = principal
            }
        };
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

    private static AccountController CreateController(TestUserManager userManager, FakeEmailSender emailSender, ChessXivDbContext? dbContext = null)
    {
        dbContext ??= CreateInMemoryDbContext();

        return new AccountController(
            userManager,
            dbContext,
            new StubQuotaService(),
            emailSender,
            Options.Create(new FrontendOptions { BaseUrl = "https://chessxiv.org" }));
    }

    private static ChessXivDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ChessXivDbContext>()
            .UseInMemoryDatabase($"AccountControllerTests-{Guid.NewGuid()}")
            .Options;

        return new ChessXivDbContext(options);
    }
}
