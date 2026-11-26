using ATM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ATM.Infrastructure.Persistence;

public sealed class DatabaseInitializer
{
    private readonly AtmDbContext _context;

    public DatabaseInitializer(AtmDbContext context)
    {
        _context = context;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await _context.Database.MigrateAsync(
            cancellationToken);

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                cancellationToken);

        var accountNames = await _context.Accounts
            .Select(account => account.Name)
            .ToListAsync(cancellationToken);

        if (!accountNames.Contains("Checking"))
        {
            _context.Accounts.Add(
                Account.Create("Checking", 1000m));
        }

        if (!accountNames.Contains("Savings"))
        {
            _context.Accounts.Add(
                Account.Create("Savings", 1000m));
        }

        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}