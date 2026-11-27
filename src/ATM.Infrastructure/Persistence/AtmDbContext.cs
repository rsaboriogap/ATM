using ATM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ATM.Infrastructure.Persistence;

public sealed class AtmDbContext : DbContext
{
    public AtmDbContext(
        DbContextOptions<AtmDbContext> options)
        : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AtmDbContext).Assembly);
    }

    // NEW: Append-Only Ledger validation.
    private void ValidateAppendOnlyLedger()
    {
        ChangeTracker.DetectChanges();

        var invalidEntries = ChangeTracker
            .Entries<Transaction>()
            .Any(entry =>
                entry.State == EntityState.Modified ||
                entry.State == EntityState.Deleted);

        if (invalidEntries)
        {
            throw new InvalidOperationException(
                "Transaction records are immutable. " +
                "Updates and deletions are not allowed.");
        }
    }

    // NEW: Synchronous persistence protection.
    public override int SaveChanges(
        bool acceptAllChangesOnSuccess)
    {
        ValidateAppendOnlyLedger();

        return base.SaveChanges(
            acceptAllChangesOnSuccess);
    }

    // NEW: Asynchronous persistence protection.
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidateAppendOnlyLedger();

        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
    }
}