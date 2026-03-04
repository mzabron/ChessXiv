using ChessBase.Application.Abstractions;

namespace ChessBase.Infrastructure.Data;

public class EfUnitOfWork(ChessBaseDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
