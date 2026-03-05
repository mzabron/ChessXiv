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
    public DbSet<Move> Moves { get; set; }
    public DbSet<Position> Positions { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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