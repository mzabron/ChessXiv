using System.Text;
using ChessXiv.Api;
using ChessXiv.Api.Authentication;
using ChessXiv.Api.Email;
using ChessXiv.Api.Hubs;
using ChessXiv.Api.Services;
using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Factories;
using ChessXiv.Domain.Engine.Serialization;
using ChessXiv.Domain.Engine.Services;
using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Repositories;
using ChessXiv.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection(FrontendOptions.SectionName));
builder.Services.Configure<BrevoOptions>(builder.Configuration.GetSection(BrevoOptions.SectionName));

var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedCorsOrigins.Length == 0)
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must include at least one allowed origin.");
        }

        policy.WithOrigins(allowedCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("AuthLogin", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("AuthForgotPassword", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    throw new InvalidOperationException("Jwt:SigningKey configuration is required.");
}

builder.Services.AddDbContext<ChessXivDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    ));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<ChessXivDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrWhiteSpace(accessToken)
                    && path.StartsWithSegments(ImportProgressHub.HubPath))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, SubOrNameIdentifierUserIdProvider>();

builder.Services.AddScoped<IPgnParser, PgnService>();
builder.Services.AddScoped<IGameRepository, GameRepository>();
builder.Services.AddScoped<IGameExplorerRepository, GameExplorerRepository>();
builder.Services.AddScoped<IDraftImportRepository, DraftImportRepository>();
builder.Services.AddScoped<IDraftPromotionRepository, DraftPromotionRepository>();
builder.Services.AddScoped<IUserDatabaseGameRepository, UserDatabaseGameRepository>();
builder.Services.AddScoped<IPositionImportCoordinator, PositionImportCoordinator>();
builder.Services.AddScoped<IBoardStateSerializer, FenBoardStateSerializer>();
builder.Services.AddScoped<IBoardStateFactory, BoardStateFactory>();
builder.Services.AddScoped<IBoardStateTransition, BitboardBoardStateTransition>();
builder.Services.AddScoped<IPositionHasher, ZobristPositionHasher>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IPgnImportService, PgnImportService>();
builder.Services.AddScoped<IDraftImportService, DraftImportService>();
builder.Services.AddScoped<IDirectDatabaseImportService, DirectDatabaseImportService>();
builder.Services.AddScoped<IDraftPromotionService, DraftPromotionService>();
builder.Services.AddScoped<IGameExplorerService, GameExplorerService>();
builder.Services.AddScoped<IPositionPlayService, PositionPlayService>();
builder.Services.AddScoped<IQuotaService, UserQuotaService>();
builder.Services.AddSingleton<DraftImportProgressCache>();
builder.Services.AddSingleton<ImportProgressConnectionRegistry>();
builder.Services.AddScoped<IDraftImportProgressPublisher, SignalRDraftImportProgressPublisher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddHttpClient<BrevoEmailSender>(httpClient =>
{
    httpClient.BaseAddress = new Uri("https://api.brevo.com");
});
builder.Services.AddScoped<LoggingEmailSender>();
builder.Services.AddScoped<IEmailSender>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<BrevoOptions>>().Value;

    if (!string.IsNullOrWhiteSpace(options.ApiKey) && !string.IsNullOrWhiteSpace(options.SenderEmail))
    {
        return serviceProvider.GetRequiredService<BrevoEmailSender>();
    }

    return serviceProvider.GetRequiredService<LoggingEmailSender>();
});
builder.Services.AddHostedService<UnconfirmedUserCleanupService>();
builder.Services.AddHostedService<StagingDraftCleanupService>();
builder.Services.AddSingleton<BackgroundImportQueue>();
builder.Services.AddHostedService<BackgroundImportWorker>();

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler");

        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        if (exceptionFeature?.Error is BadHttpRequestException badRequestException)
        {
            logger.LogWarning(badRequestException, "Bad request while processing {Path}", context.Request.Path);

            context.Response.StatusCode = badRequestException.StatusCode;

            var requestProblem = new ProblemDetails
            {
                Status = badRequestException.StatusCode,
                Title = badRequestException.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "Payload Too Large"
                    : "Bad Request",
                Detail = badRequestException.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "The uploaded PGN file is too large for this endpoint."
                    : badRequestException.Message,
                Instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(requestProblem);
            return;
        }

        if (exceptionFeature?.Error is not null)
        {
            logger.LogError(exceptionFeature.Error, "Unhandled exception while processing request {Path}", context.Request.Path);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Detail = "An unexpected error occurred. Please try again later.",
            Instance = context.Request.Path
        };

        await context.Response.WriteAsJsonAsync(problem);
    });
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseCors("Frontend");
app.UseAuthorization();

app.MapControllers();
app.MapHub<ImportProgressHub>(ImportProgressHub.HubPath);
app.Run();