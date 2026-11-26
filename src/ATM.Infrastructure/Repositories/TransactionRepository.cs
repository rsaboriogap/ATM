using ATM.Application.Interfaces.Persistence;
using ATM.Domain.Entities;
using ATM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ATM.Infrastructure.Repositories;

public sealed class TransactionRepository
    : ITransactionRepository
{
    private readonly AtmDbContext _context;

    public TransactionRepository(AtmDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Transaction>>
        GetByAccountIdAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
    {
        return await _context.Transactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.AccountId == accountId)
            .OrderByDescending(transaction =>
                transaction.CreatedAt)
            .ThenByDescending(transaction =>
                transaction.Id)
            .ToListAsync(cancellationToken);
    }

    public void Add(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        _context.Transactions.Add(transaction);
    }
}