using ATM.Application.Interfaces.Persistence;
using ATM.Domain.Entities;
using ATM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ATM.Infrastructure.Repositories;

public sealed class AccountRepository : IAccountRepository
{
    private readonly AtmDbContext _context;

    public AccountRepository(AtmDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Account>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Accounts
            .AsNoTracking()
            .OrderBy(account => account.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Account?> GetByIdAsync(
     Guid id,
     CancellationToken cancellationToken = default)
    {
        return _context.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                account => account.Id == id,
                cancellationToken);
    }

    public Task<Account?> GetForUpdateAsync(
    Guid id,
    CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A write transaction is required.");
        }

        return _context.Accounts
            .AsTracking()
            .SingleOrDefaultAsync(
                account => account.Id == id,
                cancellationToken);
    }
}