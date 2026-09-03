using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using ChessXiv.Infrastructure.Data;
using ChessXiv.Infrastructure.Repositories;
using ChessXiv.Infrastructure.Services;
using ChessXiv.Application.Abstractions;
using ChessXiv.Application.Abstractions.Repositories;
using ChessXiv.Application.Contracts;
using ChessXiv.Application.Services;
using ChessXiv.Domain.Engine.Abstractions;
using ChessXiv.Domain.Engine.Factories;
using ChessXiv.Domain.Engine.Serialization;
using ChessXiv.Domain.Engine.Services;
using Microsoft.AspNetCore.Identity;
using ChessXiv.Domain.Entities;
using System.IO;
using System.Text;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>();
// Windows consoles default to an OEM code page, which turns accented player names in the
// log into question marks. Redirected output can refuse the change, hence the guard.
try
{
	Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
	// Not a console (piped or redirected) - leave whatever the host gave us.
}

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
builder.Services.AddScoped<IPositionKeyCalculator, ZobristPositionKeyCalculator>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IUserDatabaseGameRepository, UserDatabaseGameRepository>();
builder.Services.AddScoped<IDraftPromotionRepository, DraftPromotionRepository>();
builder.Services.AddScoped<IDirectDatabaseImportService, DirectDatabaseImportService>();
builder.Services.AddScoped<IImportStatisticsRefresher, PostgresImportStatisticsRefresher>();
builder.Services.AddScoped<IGameSourceRepository, GameSourceRepository>();
builder.Services.AddScoped<IPositionRebuildRepository, PositionRebuildRepository>();
builder.Services.AddScoped<IPositionRebuildService, PositionRebuildService>();

builder.Services
	.AddIdentityCore<ApplicationUser>(options =>
	{
		options.User.RequireUniqueEmail = true;
		options.Password.RequireDigit = false;
		options.Password.RequiredLength = 6;
		options.Password.RequireNonAlphanumeric = false;
	})
	.AddEntityFrameworkStores<ChessXivDbContext>();

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

	// Maintenance mode: regenerate Positions from the PGNs already in Games. Needed once
	// after the storage-format change, and useful as a repair tool afterwards.
	if (args.Contains("--rebuild-positions", StringComparer.OrdinalIgnoreCase))
	{
		var rebuildService = services.GetRequiredService<IPositionRebuildService>();
		var progressReporter = new Progress<PositionRebuildProgress>(p =>
			logger.LogInformation("Rebuilt positions for {Processed}/{Total} games.", p.GamesProcessed, p.TotalGames));

		var rebuiltCount = await rebuildService.RebuildAsync(batchSize: 500, progressReporter);
		logger.LogInformation("Position rebuild finished for {Count} games.", rebuiltCount);
		return;
	}

	// Resolved before anything else so a typo fails immediately rather than after the
	// credentials and database prompts.
	var encodingName = ReadOptionValue(args, "--encoding");
	Encoding? forcedEncoding = null;
	if (encodingName is not null)
	{
		if (!PgnEncoding.TryResolve(encodingName, out var resolved))
		{
			logger.LogError(
				"Unknown encoding '{Encoding}'. Use a name such as utf-8, windows-1250, windows-1252 or iso-8859-2.",
				encodingName);
			Environment.ExitCode = 1;
			return;
		}

		forcedEncoding = resolved;
	}

	var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
	var dbContext = services.GetRequiredService<ChessXivDbContext>();
	var importService = services.GetRequiredService<IDirectDatabaseImportService>();
	var statisticsRefresher = services.GetRequiredService<IImportStatisticsRefresher>();
	var draftPromotionRepository = services.GetRequiredService<IDraftPromotionRepository>();
	var unitOfWork = services.GetRequiredService<IUnitOfWork>();

	Console.Write("Username or email: ");
	var username = Console.ReadLine()?.Trim();
	if (string.IsNullOrWhiteSpace(username))
	{
		logger.LogError("Username is required.");
		return;
	}

	Console.Write("Password: ");
	var password = ReadPassword();

	var user = await userManager.FindByNameAsync(username) ?? await userManager.FindByEmailAsync(username);
	if (user is null)
	{
		logger.LogError("User not found.");
		Environment.ExitCode = 1;
		return;
	}

	var pwValid = await userManager.CheckPasswordAsync(user, password);
	if (!pwValid)
	{
		logger.LogError("Invalid credentials.");
		Environment.ExitCode = 1;
		return;
	}

	var useExisting = PromptUseExistingDatabase();
	var userDatabaseId = Guid.Empty;
	var userDatabaseName = string.Empty;

	(Guid Id, string Name)? existing = null;
	if (useExisting)
	{
		existing = await PromptSelectUserDatabaseAsync(dbContext, user.Id);
		if (existing is not null)
		{
			userDatabaseId = existing.Value.Id;
			userDatabaseName = existing.Value.Name;
		}
		else
		{
			useExisting = false;
		}
	}

	if (!useExisting)
	{
		Console.Write("New database name: ");
		var dbName = Console.ReadLine()?.Trim();
		if (string.IsNullOrWhiteSpace(dbName))
		{
			logger.LogError("Database name is required.");
			return;
		}

		Console.Write("Make database public? (y/N): ");
		var isPublicInput = Console.ReadLine();
		var isPublic = !string.IsNullOrWhiteSpace(isPublicInput) && (isPublicInput.Trim().ToLowerInvariant() == "y" || isPublicInput.Trim().ToLowerInvariant() == "yes");

		var pgnPath = ResolvePgnPath(args) ?? PromptForPgnPath();
		if (pgnPath is null)
		{
			logger.LogError("PGN file not found. Provide a valid path.");
			return;
		}

		var userDatabase = new UserDatabase
		{
			Id = Guid.NewGuid(),
			Name = dbName,
			OwnerUserId = user.Id,
			IsPublic = isPublic,
			CreatedAtUtc = DateTime.UtcNow
		};

		try
		{
			dbContext.UserDatabases.Add(userDatabase);
			await unitOfWork.SaveChangesAsync();
		}
		catch (DbUpdateException ex)
		{
			logger.LogError(ex, "Failed to create user database. Name may already exist for this user.");
			Environment.ExitCode = 1;
			return;
		}

		userDatabaseId = userDatabase.Id;
		userDatabaseName = userDatabase.Name;

		logger.LogInformation("Created UserDatabase {Id} for user {User}", userDatabaseId, user.UserName);

		logger.LogInformation("Importing games from {Path}", pgnPath);
		var imported = await ImportGamesAsync(
			pgnPath, user.Id, userDatabaseId, importService, statisticsRefresher, forcedEncoding, logger);

		if (!imported)
		{
			// Only roll back when the failed import genuinely left nothing behind. Batches
			// commit as they go, so a failure part-way through leaves real games linked to
			// this database - and deleting it then cascades those links away and strands
			// every imported game in Games and Positions with nothing pointing at it:
			// invisible in the UI, impossible to delete from it, still occupying disk.
			var committedGames = await dbContext.UserDatabaseGames
				.CountAsync(link => link.UserDatabaseId == userDatabaseId);

			if (committedGames == 0)
			{
				dbContext.UserDatabases.Remove(userDatabase);
				await unitOfWork.SaveChangesAsync();
				logger.LogInformation("Rolled back the empty database {Name} created for this import.", userDatabaseName);
			}
			else
			{
				// The count is normally stamped only on a successful run, so do it here -
				// otherwise the database shows 0 games while actually holding thousands.
				await draftPromotionRepository.SyncGameCountAsync(userDatabaseId);
				logger.LogWarning(
					"Import failed, but {Count} games had already been committed. Keeping database {Name} ({Id}) " +
					"so they stay reachable - delete it from the UI if you intend to start over.",
					committedGames,
					userDatabaseName,
					userDatabaseId);
			}
		}

		return;
	}

	var pgnPathExisting = ResolvePgnPath(args) ?? PromptForPgnPath();
	if (pgnPathExisting is null)
	{
		logger.LogError("PGN file not found. Provide a valid path.");
		return;
	}

	logger.LogInformation("Using UserDatabase {Name} ({Id})", userDatabaseName, userDatabaseId);
	logger.LogInformation("Importing games from {Path}", pgnPathExisting);

	if (await ImportGamesAsync(
		pgnPathExisting, user.Id, userDatabaseId, importService, statisticsRefresher, forcedEncoding, logger))
	{
		logger.LogInformation("Games added to existing database.");
	}

	return;
}
catch (Exception ex)
{
	Console.Error.WriteLine(ex.Message);
	logger.LogError(ex, "CLI import failed.");
	Environment.ExitCode = 1;
}

static string ReadPassword()
{
	// A multi-gigabyte import runs for hours, so it has to be possible to start it from a
	// script, an ssh command or under nohup. Console.ReadKey throws outright when stdin is
	// redirected, which made every one of those impossible.
	if (Console.IsInputRedirected)
	{
		Console.WriteLine();
		return Console.ReadLine() ?? string.Empty;
	}

	var sb = new StringBuilder();
	while (true)
	{
		var key = Console.ReadKey(true);
		if (key.Key == ConsoleKey.Enter)
		{
			Console.WriteLine();
			break;
		}

		if (key.Key == ConsoleKey.Backspace)
		{
			if (sb.Length > 0)
			{
				sb.Length--;
				Console.Write("\b \b");
			}
			continue;
		}

		sb.Append(key.KeyChar);
		Console.Write('*');
	}

	return sb.ToString();
}

static bool PromptUseExistingDatabase()
{
	Console.Write("Use existing database? (y/N): ");
	var input = Console.ReadLine();
	if (string.IsNullOrWhiteSpace(input))
	{
		return false;
	}

	var normalized = input.Trim().ToLowerInvariant();
	return normalized is "y" or "yes";
}

static async Task<(Guid Id, string Name)?> PromptSelectUserDatabaseAsync(
	ChessXivDbContext dbContext,
	string ownerUserId)
{
	var databases = await dbContext.UserDatabases
		.AsNoTracking()
		.Where(d => d.OwnerUserId == ownerUserId)
		.OrderBy(d => d.Name)
		.Select(d => new { d.Id, d.Name })
		.ToListAsync();

	if (databases.Count == 0)
	{
		Console.WriteLine("No existing databases found. You must create a new one.");
		return null;
	}

	Console.WriteLine("Available databases:");
	for (var i = 0; i < databases.Count; i++)
	{
		Console.WriteLine($"  {i + 1}. {databases[i].Name}");
	}

	while (true)
	{
		Console.Write("Choose database by number or name (empty to cancel): ");
		var input = Console.ReadLine()?.Trim();
		if (string.IsNullOrWhiteSpace(input))
		{
			return null;
		}

		if (int.TryParse(input, out var index) && index >= 1 && index <= databases.Count)
		{
			var selected = databases[index - 1];
			return (selected.Id, selected.Name);
		}

		var nameMatch = databases.FirstOrDefault(d =>
			string.Equals(d.Name, input, StringComparison.OrdinalIgnoreCase));
		if (nameMatch is not null)
		{
			return (nameMatch.Id, nameMatch.Name);
		}

		Console.WriteLine("Invalid selection. Try again.");
	}
}

static async Task<bool> ImportGamesAsync(
	string pgnPath,
	string ownerUserId,
	Guid userDatabaseId,
	IDirectDatabaseImportService importService,
	IImportStatisticsRefresher statisticsRefresher,
	Encoding? forcedEncoding,
	ILogger logger)
{
	// The CLI runs the same import the web upload does, so both get binary COPY and
	// per-batch commits, and there is one code path to keep correct.
	using var fileStream = File.OpenRead(pgnPath);
	using var reader = PgnEncoding.OpenReader(fileStream, forcedEncoding, out var encoding);
	logger.LogInformation(
		"Reading {Path} as {Encoding}{Source}.",
		pgnPath,
		encoding.WebName,
		forcedEncoding is null ? " (detected)" : " (--encoding)");

	var progress = new Progress<ImportProgress>(p =>
		logger.LogInformation(
			"Imported {Imported} games ({Parsed} parsed, {Skipped} skipped)...",
			p.ImportedCount,
			p.ParsedCount,
			p.SkippedCount));

	try
	{
		var result = await importService.ImportToDatabaseAsync(
			reader,
			ownerUserId,
			userDatabaseId,
			batchSize: 500,
			progress: progress);

		// Same post-import ANALYZE the web worker runs. Skipping it here left the planner
		// working from stale statistics after the largest imports in the system.
		await statisticsRefresher.RefreshAfterDatabaseImportAsync();

		logger.LogInformation(
			"Import finished. Parsed: {Parsed}, Imported: {Imported}, Skipped: {Skipped}",
			result.ParsedCount,
			result.ImportedCount,
			result.SkippedCount);
		return true;
	}
	catch (Exception ex)
	{
		logger.LogError(ex, "Import failed.");
		Environment.ExitCode = 1;
		return false;
	}
}

/// <summary>
/// Reads "--name value" or "--name=value" out of the argument list.
/// </summary>
static string? ReadOptionValue(string[] args, string name)
{
	for (var i = 0; i < args.Length; i++)
	{
		if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
		{
			return args[i][(name.Length + 1)..];
		}

		if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
		{
			return args[i + 1];
		}
	}

	return null;
}

static string? ResolvePgnPath(string[] args)
{
	// The path is the first token that is neither a flag nor the value belonging to one,
	// so "--encoding windows-1250 games.pgn" resolves the same as "games.pgn".
	for (var i = 0; i < args.Length; i++)
	{
		if (args[i].StartsWith("--", StringComparison.Ordinal))
		{
			// Skip this flag's separate value, if it takes one.
			if (!args[i].Contains('=', StringComparison.Ordinal)
				&& string.Equals(args[i], "--encoding", StringComparison.OrdinalIgnoreCase))
			{
				i++;
			}

			continue;
		}

		var candidate = Path.GetFullPath(args[i]);
		if (File.Exists(candidate))
		{
			return candidate;
		}

		break;
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

	return null;
}

static string? PromptForPgnPath()
{
	while (true)
	{
		Console.Write("PGN file path (empty to cancel): ");
		var input = Console.ReadLine()?.Trim();
		if (string.IsNullOrWhiteSpace(input))
		{
			return null;
		}

		var candidate = Path.GetFullPath(input);
		if (File.Exists(candidate))
		{
			return candidate;
		}

		Console.WriteLine("File not found. Please try again.");
	}
}