using Microsoft.EntityFrameworkCore;
using ChessXiv.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace ChessXiv.Infrastructure.Data;

public class ChessXivDbContext : IdentityDbContext<ApplicationUser>
{
    public ChessXivDbContext(DbContextOptions<ChessXivDbContext> options)
        : base(options)
    {
    }

    public DbSet<Game> Games { get; set; }
    public DbSet<Player> Players { get; set; }
    public DbSet<Move> Moves { get; set; }
    public DbSet<Position> Positions { get; set; }
    public DbSet<UserDatabase> UserDatabases { get; set; }
    public DbSet<UserDatabaseGame> UserDatabaseGames { get; set; }
    public DbSet<UserDatabaseBookmark> UserDatabaseBookmarks { get; set; }
    public DbSet<StagingImportSession> StagingImportSessions { get; set; }
    public DbSet<StagingGame> StagingGames { get; set; }
    public DbSet<StagingMove> StagingMoves { get; set; }
    public DbSet<StagingPosition> StagingPositions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.CreatedAtUtc).IsRequired();
            entity.Property(u => u.UserTier).HasMaxLength(32).IsRequired();
        });

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
            entity.Property(g => g.GameHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(g => g.GameHash);

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
            entity.HasIndex(p => p.Fen);
            entity.HasIndex(p => new {p.GameId, p.PlyCount });
            entity
                .HasOne(p => p.Game)
                .WithMany(g => g.Positions)
                .HasForeignKey(p => p.GameId);

        });

        modelBuilder.Entity<UserDatabase>(entity =>
        {
            entity.Property(d => d.Name).HasMaxLength(200).IsRequired();
            entity.Property(d => d.OwnerUserId).IsRequired();
            entity.Property(d => d.CreatedAtUtc).IsRequired();

            entity.HasIndex(d => new { d.OwnerUserId, d.Name }).IsUnique();
            entity.HasIndex(d => d.IsPublic);

            entity
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(d => d.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserDatabaseGame>(entity =>
        {
            entity.HasKey(x => new { x.UserDatabaseId, x.GameId });
            entity.Property(x => x.AddedAtUtc).IsRequired();
            entity.Property(x => x.Event).HasMaxLength(300);
            entity.Property(x => x.Round).HasMaxLength(64);
            entity.Property(x => x.Site).HasMaxLength(300);

            entity
                .HasOne(x => x.UserDatabase)
                .WithMany(d => d.UserDatabaseGames)
                .HasForeignKey(x => x.UserDatabaseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(x => x.Game)
                .WithMany(g => g.UserDatabaseGames)
                .HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserDatabaseBookmark>(entity =>
        {
            entity.HasKey(x => new { x.UserId, x.UserDatabaseId });
            entity.Property(x => x.CreatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            entity.HasIndex(x => x.UserDatabaseId);

            entity
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(x => x.UserDatabase)
                .WithMany(d => d.Bookmarks)
                .HasForeignKey(x => x.UserDatabaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StagingImportSession>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OwnerUserId).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.ExpiresAtUtc).IsRequired();
            entity.HasIndex(x => new { x.OwnerUserId, x.CreatedAtUtc });
            entity.HasIndex(x => x.ExpiresAtUtc);

            entity
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StagingGame>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OwnerUserId).IsRequired();
            entity.Property(x => x.White).IsRequired();
            entity.Property(x => x.Black).IsRequired();
            entity.Property(x => x.Result).IsRequired();
            entity.Property(x => x.Pgn).IsRequired();
            entity.Property(x => x.GameHash).HasMaxLength(64).IsRequired();

            entity.HasIndex(x => new { x.OwnerUserId, x.ImportSessionId, x.GameHash });
            entity.HasIndex(x => new { x.OwnerUserId, x.ImportSessionId, x.White });
            entity.HasIndex(x => new { x.OwnerUserId, x.ImportSessionId, x.Black });
            entity.HasIndex(x => x.ImportSessionId);

            entity
                .HasOne(x => x.ImportSession)
                .WithMany(x => x.Games)
                .HasForeignKey(x => x.ImportSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StagingMove>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity
                .HasOne(x => x.Game)
                .WithMany(x => x.Moves)
                .HasForeignKey(x => x.StagingGameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StagingPosition>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.FenHash);
            entity.HasIndex(x => new { x.StagingGameId, x.PlyCount });

            entity
                .HasOne(x => x.Game)
                .WithMany(x => x.Positions)
                .HasForeignKey(x => x.StagingGameId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}