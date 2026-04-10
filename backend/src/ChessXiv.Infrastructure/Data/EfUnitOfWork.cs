using ChessXiv.Application.Abstractions;

namespace ChessXiv.Infrastructure.Data;

public class EfUnitOfWork(ChessXivDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new EfUnitOfWorkTransaction(transaction);
    }

    public void ClearTracker()
    {
        dbContext.ChangeTracker.Clear();
    }
}
