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
}