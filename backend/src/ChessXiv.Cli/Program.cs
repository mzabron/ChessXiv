using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Repositories;
using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Factories;
using ChessXiv.Domain.Engine.Serialization;
using ChessXiv.Domain.Engine.Services;
using System.IO;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
	options.SingleLine = true;
	options.TimestampFormat = "HH:mm:ss ";
});

var connectionString = builder.Configuration["CHESSXIV_CONNECTION_STRING"] 
                       ?? builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("Warning: Connection string not found. Ensure it is set in appsettings.json or Environment Variables.");
}

builder.Services.AddDbContext<ChessXivDbContext>(options => 
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
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ChessXiv.Cli");

try
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        logger.LogError("Cannot proceed: No connection string provided.");
        return;
    }
	using var scope = host.Services.CreateScope();
	var services = scope.ServiceProvider;
	var importService = services.GetRequiredService<IPgnImportService>();

	var pgnPath = ResolvePgnPath(args);
	if (pgnPath is null)
	{
		logger.LogError("PGN file not found. Provide path as first argument or set CHESSXIV_PGN_PATH.");
		return;
	}

	logger.LogInformation("Importing games from {Path}", pgnPath);
	using var reader = new StreamReader(pgnPath);
	var result = await importService.ImportAsync(reader, markAsMaster: true);

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

	var envPath = Environment.GetEnvironmentVariable("CHESSXIV_PGN_PATH");
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