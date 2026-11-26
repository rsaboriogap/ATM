using ATM.Application.Interfaces.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ATM.Infrastructure.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AtmDbContext _context;

    public EfUnitOfWork(AtmDbContext context)
    {
        _context = context;
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Nested transactions are not supported.");
        }

        if (_context.ChangeTracker.Entries().Any())
        {
            throw new InvalidOperationException(
                "The transaction requires a clean DbContext.");
        }

        await _context.Database.OpenConnectionAsync(
            cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var connection = (SqliteConnection)
                _context.Database.GetDbConnection();

            // Acquire the SQLite write lock before reading balances.
            await using var sqliteTransaction =
                connection.BeginTransaction(deferred: false);

            // Enlist EF Core in the same SQLite transaction.
            await using var transaction =
                await _context.Database.UseTransactionAsync(
                    sqliteTransaction,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Unable to enlist EF Core in the transaction.");

            try
            {
                var result = await operation(cancellationToken);

                await _context.SaveChangesAsync(
                    cancellationToken);

                await transaction.CommitAsync(
                    cancellationToken);

                return result;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(
                        CancellationToken.None);
                }
                finally
                {
                    // Remove entities containing rolled-back changes.
                    _context.ChangeTracker.Clear();
                }

                throw;
            }
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }
}