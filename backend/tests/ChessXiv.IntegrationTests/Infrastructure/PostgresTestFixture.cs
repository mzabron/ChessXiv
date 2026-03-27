using ChessXiv.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ChessXiv.IntegrationTests.Infrastructure;

public sealed class PostgresTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public PostgresTestFixture()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("chessxiv_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public ChessXivDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ChessXivDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ChessXivDbContext(options);
    }

    public async Task ResetDatabaseAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE " +
            "\"StagingPositions\", \"StagingMoves\", \"StagingGames\", " +
            "\"UserDatabaseGames\", \"UserDatabases\", " +
            "\"Positions\", \"Moves\", \"Games\", " +
            "\"AspNetUserTokens\", \"AspNetUserRoles\", \"AspNetUserLogins\", \"AspNetUserClaims\", \"AspNetUsers\", " +
            "\"AspNetRoleClaims\", \"AspNetRoles\" " +
            "RESTART IDENTITY CASCADE;");
    }
}
