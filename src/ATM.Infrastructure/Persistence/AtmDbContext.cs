using ATM.Domain.Entities;
using ATM.Infrastructure.Logging;
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

    public DbSet<LogEntry> Logs => Set<LogEntry>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AtmDbContext).Assembly);
    }
}