using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using ChessBase.Infrastructure.Data;
using ChessBase.Infrastructure.Repositories;
using ChessBase.Application.Abstractions;
using ChessBase.Application.Abstractions.Repositories;
using ChessBase.Application.Services;
using ChessBase.Domain.Engine.Abstractions;
using ChessBase.Domain.Engine.Factories;
using ChessBase.Domain.Engine.Serialization;
using ChessBase.Domain.Engine.Services;
using System.IO;
using System.Linq;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
	options.SingleLine = true;
	options.TimestampFormat = "HH:mm:ss ";
});

var connectionString = builder.Configuration["CHESSBASE_CONNECTION_STRING"] 
                       ?? builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("Warning: Connection string not found. Ensure it is set in appsettings.json or Environment Variables.");
}

builder.Services.AddDbContext<ChessBaseDbContext>(options => 
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseNpgsql(connectionString);
    }
});
builder.Services.AddScoped<IPgnParser, PgnService>();
builder.Services.AddScoped<IGameRepository, GameRepository>();
builder.Services.AddScoped<IPositionImportCoordinator, PositionImportCoordinator>();
builder.Services.AddScoped<IBoardStateSerializer, FenBoardStateSerializer>();
builder.Services.AddScoped<IBoardStateFactory, BoardStateFactory>();
builder.Services.AddScoped<IBoardStateTransition, BitboardBoardStateTransition>();
builder.Services.AddScoped<IPositionHasher, ZobristPositionHasher>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IPgnImportService, PgnImportService>();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ChessBase.Cli");

try
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        logger.LogError("Cannot proceed: No connection string provided.");
        return;
    }
	using var scope = host.Services.CreateScope();
	var services = scope.ServiceProvider;
	var dbContext = services.GetRequiredService<ChessBaseDbContext>();
	var importService = services.GetRequiredService<IPgnImportService>();

	if (ShouldApplyMigrations(args))
	{
		logger.LogInformation("Applying database migrations...");
		await dbContext.Database.MigrateAsync();
	}
	else
	{
		logger.LogInformation("Skipping migrations. Pass --migrate to apply migrations.");
	}

	var pgnPath = ResolvePgnPath(args);
	if (pgnPath is null)
	{
		logger.LogError("PGN file not found. Provide path as first argument or set CHESSBASE_PGN_PATH.");
		return;
	}

	logger.LogInformation("Importing games from {Path}", pgnPath);
	var pgnContent = await File.ReadAllTextAsync(pgnPath);
	var result = await importService.ImportAsync(pgnContent);

	logger.LogInformation(
		"Import finished. Parsed: {Parsed}, Imported: {Imported}, Skipped: {Skipped}",
		result.ParsedCount,
		result.ImportedCount,
		result.SkippedCount);
}
catch (Exception ex)
{
	logger.LogError(ex, "CLI import failed.");
	Environment.ExitCode = 1;
}

static bool ShouldApplyMigrations(string[] args)
{
	if (args.Any(arg => string.Equals(arg, "--migrate", StringComparison.OrdinalIgnoreCase)))
	{
		return true;
	}

	var env = Environment.GetEnvironmentVariable("CHESSBASE_APPLY_MIGRATIONS");
	return bool.TryParse(env, out var apply) && apply;
}

static string? ResolvePgnPath(string[] args)
{
	if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
	{
		var candidate = Path.GetFullPath(args[0]);
		if (File.Exists(candidate))
		{
			return candidate;
		}
	}

	var envPath = Environment.GetEnvironmentVariable("CHESSBASE_PGN_PATH");
	if (!string.IsNullOrWhiteSpace(envPath))
	{
		var candidate = Path.GetFullPath(envPath);
		if (File.Exists(candidate))
		{
			return candidate;
		}
	}

	return FindInAncestors("backend/tests/TestData/games_sample.pgn");
}

static string? FindInAncestors(string relativePath)
{
	var startPoints = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
	foreach (var start in startPoints)
	{
		var current = new DirectoryInfo(Path.GetFullPath(start));
		while (current is not null)
		{
			var candidate = Path.Combine(current.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(candidate))
			{
				return candidate;
			}

			current = current.Parent;
		}
	}

	return null;
}