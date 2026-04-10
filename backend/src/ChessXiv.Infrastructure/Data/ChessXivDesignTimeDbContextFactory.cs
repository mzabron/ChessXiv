using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ChessXiv.Infrastructure.Data;

public class ChessXivDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ChessXivDbContext>
{
    public ChessXivDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ChessXivDbContext>();

        var apiProjectDirectory = ResolveApiProjectDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiProjectDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["CHESSXIV_CONNECTION_STRING"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Unable to resolve connection string for design-time EF operations. " +
                "Set ConnectionStrings:DefaultConnection in src/ChessXiv.Api/appsettings.json " +
                "or set CHESSXIV_CONNECTION_STRING in environment variables.");
        }

        optionsBuilder.UseNpgsql(connectionString);
        return new ChessXivDbContext(optionsBuilder.Options);
    }

    private static string ResolveApiProjectDirectory()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var start in candidates)
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                var apiPath = Path.Combine(current.FullName, "src", "ChessXiv.Api");
                if (Directory.Exists(apiPath))
                {
                    return apiPath;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate src/ChessXiv.Api directory for loading appsettings.json during design-time DbContext creation.");
    }
}
