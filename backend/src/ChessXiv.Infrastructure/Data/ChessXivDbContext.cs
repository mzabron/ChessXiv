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
    public DbSet<Position> Positions { get; set; }
    public DbSet<UserDatabase> UserDatabases { get; set; }
    public DbSet<UserDatabaseGame> UserDatabaseGames { get; set; }
    public DbSet<UserDatabaseBookmark> UserDatabaseBookmarks { get; set; }
    public DbSet<StagingGame> StagingGames { get; set; }
    public DbSet<StagingPosition> StagingPositions { get; set; }
    public DbSet<StagingDraftSession> StagingDraftSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasIndex(g => new { g.Year, g.Id });
            entity.HasIndex(g => g.MoveCount);
            entity.Property(g => g.GameHash).HasMaxLength(64).IsRequired();
            entity.Property(g => g.WhiteNormalizedFullName).HasMaxLength(256).IsRequired();
            entity.Property(g => g.BlackNormalizedFullName).HasMaxLength(256).IsRequired();
            entity.Property(g => g.WhiteNormalizedFirstName).HasMaxLength(128);
            entity.Property(g => g.WhiteNormalizedLastName).HasMaxLength(128);
            entity.Property(g => g.BlackNormalizedFirstName).HasMaxLength(128);
            entity.Property(g => g.BlackNormalizedLastName).HasMaxLength(128);
            entity.HasIndex(g => g.GameHash);
            entity.HasIndex(g => new { g.WhiteNormalizedFirstName, g.WhiteNormalizedLastName });
            entity.HasIndex(g => new { g.BlackNormalizedFirstName, g.BlackNormalizedLastName });
        });

        modelBuilder.Entity<Position>(entity =>
        {
            // Clustering by game keeps a game's positions together, which is what cascade
            // deletes and replay reads touch, and removes the surrogate uuid key entirely.
            entity.HasKey(p => new { p.GameId, p.PlyCount });
            entity.Property(p => p.PosKey).HasColumnType("bytea").IsRequired();
            entity.Property(p => p.Result).HasConversion<byte>().IsRequired();

            // Covering index: position search and the opening tree are both answered from
            // the index alone, without visiting the heap.
            //
            // GameId is included even though it is already the leading key column of the
            // primary key. The opening tree joins Positions to UserDatabaseGames on GameId
            // to restrict a position to one database, and an index that cannot supply
            // GameId forces that join to read the heap - once per matching row. At the
            // starting position, which every game contains, the planner measured that as
            // worse than reading the whole table and picked a sequential scan over 89M
            // rows: 34 seconds, past the command timeout, so the request 500'd. With
            // GameId here both sides of the join are index-only scans.
            entity.HasIndex(p => p.PosKey)
                .IncludeProperties(p => new { p.NextMove, p.Result, p.GameId });

            entity
                .HasOne(p => p.Game)
                .WithMany(g => g.Positions)
                .HasForeignKey(p => p.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserDatabase>(entity =>
        {
            entity.Property(d => d.Name).HasMaxLength(200).IsRequired();
            entity.Property(d => d.OwnerUserId).IsRequired();
            entity.Property(d => d.CreatedAtUtc).IsRequired();
            entity.Property(d => d.GameCount).IsRequired().HasDefaultValue(0);
            entity.Property(d => d.ContentUpdatedAtUtc).IsRequired();

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

        modelBuilder.Entity<StagingGame>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OwnerUserId).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.White).IsRequired();
            entity.Property(x => x.Black).IsRequired();
            entity.Property(x => x.Result).IsRequired();
            entity.Property(x => x.Pgn).IsRequired();
            entity.Property(x => x.GameHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.WhiteNormalizedFullName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.BlackNormalizedFullName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.WhiteNormalizedFirstName).HasMaxLength(128);
            entity.Property(x => x.WhiteNormalizedLastName).HasMaxLength(128);
            entity.Property(x => x.BlackNormalizedFirstName).HasMaxLength(128);
            entity.Property(x => x.BlackNormalizedLastName).HasMaxLength(128);

            entity.HasIndex(x => new { x.OwnerUserId, x.GameHash });
            entity.HasIndex(x => new { x.OwnerUserId, x.White });
            entity.HasIndex(x => new { x.OwnerUserId, x.Black });
            entity.HasIndex(x => new { x.OwnerUserId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.OwnerUserId, x.WhiteNormalizedFirstName, x.WhiteNormalizedLastName });
            entity.HasIndex(x => new { x.OwnerUserId, x.BlackNormalizedFirstName, x.BlackNormalizedLastName });
        });

        modelBuilder.Entity<StagingDraftSession>(entity =>
        {
            entity.HasKey(x => x.OwnerUserId);
            entity.Property(x => x.OwnerUserId).HasMaxLength(128);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.LastAccessedAtUtc).IsRequired();
            entity.HasIndex(x => x.LastAccessedAtUtc);
        });

        modelBuilder.Entity<StagingPosition>(entity =>
        {
            entity.HasKey(x => new { x.StagingGameId, x.PlyCount });
            entity.Property(x => x.PosKey).HasColumnType("bytea").IsRequired();
            entity.Property(x => x.Result).HasConversion<byte>().IsRequired();

            // Same join-covering reason as Positions above, with StagingGameId as the key
            // the draft move tree joins on.
            entity.HasIndex(x => x.PosKey)
                .IncludeProperties(x => new { x.NextMove, x.Result, x.StagingGameId });

            entity
                .HasOne(x => x.Game)
                .WithMany(x => x.Positions)
                .HasForeignKey(x => x.StagingGameId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}