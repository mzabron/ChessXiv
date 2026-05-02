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
using Microsoft.AspNetCore.Identity;
using ChessXiv.Domain.Entities;
using System.IO;
using System.Text;

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
builder.Services.AddScoped<IUserDatabaseGameRepository, UserDatabaseGameRepository>();

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

	var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
	var dbContext = services.GetRequiredService<ChessXivDbContext>();
	var pgnParser = services.GetRequiredService<IPgnParser>();
	var positionImportCoordinator = services.GetRequiredService<IPositionImportCoordinator>();
	var gameRepository = services.GetRequiredService<IGameRepository>();
	var userDatabaseGameRepository = services.GetRequiredService<IUserDatabaseGameRepository>();
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

	logger.LogInformation("Importing games from {Path}", pgnPath);

	using var reader = new StreamReader(pgnPath);
	var parsedCount = 0;
	var importedCount = 0;
	var skippedCount = 0;
	var batchSize = 500;
	var batch = new List<Game>(batchSize);

	await using var transaction = await unitOfWork.BeginTransactionAsync();
	try
	{
		await foreach (var game in pgnParser.ParsePgnAsync(reader))
		{
			parsedCount++;
			if (string.IsNullOrWhiteSpace(game.White) || string.IsNullOrWhiteSpace(game.Black))
			{
				skippedCount++;
				continue;
			}

			game.IsMaster = true;
			batch.Add(game);
			importedCount++;

			if (batch.Count >= batchSize)
			{
				await PersistBatchAndLinkAsync(batch, userDatabase.Id, positionImportCoordinator, gameRepository, userDatabaseGameRepository, unitOfWork);
				batch.Clear();
			}
		}

		if (batch.Count > 0)
		{
			await PersistBatchAndLinkAsync(batch, userDatabase.Id, positionImportCoordinator, gameRepository, userDatabaseGameRepository, unitOfWork);
			batch.Clear();
		}

		await transaction.CommitAsync();
	}
	catch (Exception ex)
	{
		await transaction.RollbackAsync();
		logger.LogError(ex, "Import failed.");
		Environment.ExitCode = 1;
		return;
	}

	logger.LogInformation("Import finished. Parsed: {Parsed}, Imported: {Imported}, Skipped: {Skipped}", parsedCount, importedCount, skippedCount);
	logger.LogInformation("Created UserDatabase {Id} for user {User}", userDatabase.Id, user.UserName);
}
catch (Exception ex)
{
	Console.Error.WriteLine(ex.Message);
	logger.LogError(ex, "CLI import failed.");
	Environment.ExitCode = 1;
}

static string ReadPassword()
{
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
				Console.Write("\\b \\b");
			}
			continue;
		}

		sb.Append(key.KeyChar);
		Console.Write('*');
	}

	return sb.ToString();
}

static async Task PersistBatchAndLinkAsync(
	IReadOnlyCollection<Game> games,
	Guid userDatabaseId,
	IPositionImportCoordinator positionImportCoordinator,
	IGameRepository gameRepository,
	IUserDatabaseGameRepository userDatabaseGameRepository,
	IUnitOfWork unitOfWork)
{
	var addedAtUtc = DateTime.UtcNow;

	foreach (var game in games)
	{
		if (game.Date.HasValue)
		{
			game.Year = game.Date.Value.Year;
		}

		game.MoveCount = game.Moves.Count;
		ApplyNormalizedNames(game);
		game.GameHash = GameHashCalculator.Compute(game);
	}

	await positionImportCoordinator.PopulateAsync(games);
	await gameRepository.AddRangeAsync(games);

	var links = games.Select(g => new UserDatabaseGame
	{
		UserDatabaseId = userDatabaseId,
		GameId = g.Id,
		AddedAtUtc = addedAtUtc,
		Date = g.Date,
		Year = g.Year <= 0 ? null : g.Year,
		Event = g.Event,
		Round = g.Round,
		Site = g.Site
	}).ToArray();

	await userDatabaseGameRepository.AddRangeAsync(links);
	await unitOfWork.SaveChangesAsync();
	unitOfWork.ClearTracker();
}

static void ApplyNormalizedNames(Game game)
{
	static (string? FirstName, string? LastName) ParseNameParts(string fullName)
	{
		if (string.IsNullOrWhiteSpace(fullName))
		{
			return (null, null);
		}

		var normalized = fullName.Trim();
		if (normalized.Contains(','))
		{
			var parts = normalized.Split(',', 2, StringSplitOptions.TrimEntries);
			var last = parts[0];
			var first = parts.Length > 1 ? parts[1] : null;
			return (string.IsNullOrWhiteSpace(first) ? null : first, string.IsNullOrWhiteSpace(last) ? null : last);
		}

		var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (tokens.Length == 1)
		{
			return (tokens[0], null);
		}

		var firstName = tokens[0];
		var lastName = tokens[^1];
		return (firstName, lastName);
	}

	static string NormalizeName(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
		var trimmed = raw.Trim();
		var normalized = trimmed.Normalize(System.Text.NormalizationForm.FormD);
		var builder = new StringBuilder(normalized.Length);

		foreach (var ch in normalized)
		{
			var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
			if (category == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
			var lowered = char.ToLowerInvariant(ch);
			builder.Append(lowered switch
			{
				'ł' => 'l',
				'đ' => 'd',
				'ð' => 'd',
				_ => lowered
			});
		}

		var compact = System.Text.RegularExpressions.Regex.Replace(builder.ToString(), "\\s+", " ").Trim();
		return compact;
	}

	void Apply(string rawName, out string full, out string? first, out string? last)
	{
		var (parsedFirst, parsedLast) = ParseNameParts(rawName);
		first = parsedFirst is null ? null : NormalizeName(parsedFirst);
		last = parsedLast is null ? null : NormalizeName(parsedLast);

		if (first is not null && last is not null)
		{
			full = NormalizeName($"{parsedFirst} {parsedLast}");
			return;
		}

		full = NormalizeName(rawName);
	}

	Apply(game.White, out var wf, out var w1, out var w2);
	Apply(game.Black, out var bf, out var b1, out var b2);

	game.WhiteNormalizedFullName = wf;
	game.WhiteNormalizedFirstName = w1;
	game.WhiteNormalizedLastName = w2;
	game.BlackNormalizedFullName = bf;
	game.BlackNormalizedFirstName = b1;
	game.BlackNormalizedLastName = b2;
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