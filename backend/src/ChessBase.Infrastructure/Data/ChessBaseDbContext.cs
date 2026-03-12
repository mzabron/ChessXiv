using Microsoft.EntityFrameworkCore;
using ChessBase.Domain.Entities;

namespace ChessBase.Infrastructure.Data;

public class ChessBaseDbContext : DbContext
{
    public ChessBaseDbContext(DbContextOptions<ChessBaseDbContext> options)
        : base(options)
    {
    }

    public DbSet<Game> Games { get; set; }
    public DbSet<Player> Players { get; set; }
    public DbSet<Move> Moves { get; set; }
    public DbSet<Position> Positions { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasIndex(p => p.NormalizedFullName).IsUnique();
            entity.HasIndex(p => p.NormalizedFirstName);
            entity.HasIndex(p => p.NormalizedLastName);
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasIndex(g => new { g.Year, g.Id });
            entity.HasIndex(g => g.MoveCount);

            entity
                .HasOne(g => g.WhitePlayer)
                .WithMany(p => p.GamesAsWhite)
                .HasForeignKey(g => g.WhitePlayerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity
                .HasOne(g => g.BlackPlayer)
                .WithMany(p => p.GamesAsBlack)
                .HasForeignKey(g => g.BlackPlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Move>(entity =>
        {
            entity
                .HasOne(m => m.Game)
                .WithMany(g => g.Moves)
                .HasForeignKey(m => m.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Position>(entity =>
        {
            entity.HasIndex(p => p.FenHash);
            entity.HasIndex(p => new {p.GameId, p.PlyCount });
            entity
                .HasOne(p => p.Game)
                .WithMany(g => g.Positions)
                .HasForeignKey(p => p.GameId);

        });
    }
}