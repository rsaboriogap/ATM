using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ATM.Tests.Integration.Persistence;

public sealed class AtmDbContextTests
{
    [Fact]
    public async Task SaveAccount_ValidAccount_PersistsBalance()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AtmDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AtmDbContext(options);

        await context.Database.EnsureCreatedAsync();

        var (account, openingEntry) = Account.Open("Checking", 1250.75m);

        context.Accounts.Add(account);
        context.Transactions.Add(openingEntry);

        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        var savedAccount = await context.Accounts
            .SingleAsync();

        Assert.Equal(account.Id, savedAccount.Id);
        Assert.Equal(1250.75m, savedAccount.Balance);
        Assert.Equal("USD", savedAccount.Currency);
    }

    [Fact]
    public async Task SaveAccount_Balance_IsStoredAsIntegerCents()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AtmDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AtmDbContext(options);

        await context.Database.EnsureCreatedAsync();

        var (account, openingEntry) = Account.Open("Checking", 1250.75m);

        context.Accounts.Add(account);
        context.Transactions.Add(openingEntry);

        await context.SaveChangesAsync();

        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT Balance FROM Accounts";

        var result = await command.ExecuteScalarAsync();

        Assert.Equal(125075L, Assert.IsType<long>(result));
    }

    [Fact]
    public async Task SaveTransaction_ValidTransaction_Persists()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AtmDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AtmDbContext(options);

        await context.Database.EnsureCreatedAsync();

        var (account, openingEntry) = Account.Open("Checking", 1000m);

        context.Accounts.Add(account);
        context.Transactions.Add(openingEntry);

        await context.SaveChangesAsync();

        var transaction = account.Deposit(150m);

        context.Transactions.Add(transaction);

        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        var savedTransaction = await context.Transactions
            .SingleAsync(t => t.Type == TransactionType.Deposit);

        Assert.Equal(account.Id, savedTransaction.AccountId);
        Assert.Equal(150m, savedTransaction.Amount);
        Assert.Equal(1150m, savedTransaction.BalanceAfter);
    }

    [Fact]
    public async Task SaveAccount_DuplicateName_IsRejected()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AtmDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AtmDbContext(options);

        await context.Database.EnsureCreatedAsync();

        context.Accounts.Add(
            Account.Open("Checking", 1000m).Account);

        await context.SaveChangesAsync();

        context.Accounts.Add(
            Account.Open("Checking", 500m).Account);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task InitializeDatabase_MultipleTimes_DoesNotResetAccounts()
    {
        // The initializer requires a file-backed WAL database, so this
        // cannot use an in-memory connection.
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using (var context = database.CreateContext())
        {
            var account = await context.Accounts
                .SingleAsync(a => a.Name == "Checking");

            // The deposit returns its ledger entry; the DB trigger then
            // updates Accounts.Balance from it.
            context.Transactions.Add(account.Deposit(250m));

            await context.SaveChangesAsync();
        }

        await database.InitializeAsync();

        await using var verification = database.CreateContext();

        Assert.Equal(2, await verification.Accounts.CountAsync());

        var savedAccount = await verification.Accounts
            .SingleAsync(a => a.Name == "Checking");

        Assert.Equal(1250m, savedAccount.Balance);

        // Re-running the initializer must not seed a second opening entry.
        var openingEntries = await verification.Transactions
            .Where(t => t.Type == TransactionType.OpeningBalance)
            .ToListAsync();

        Assert.Equal(2, openingEntries.Count);

        Assert.Equal(
            2,
            openingEntries.Select(t => t.AccountId).Distinct().Count());

        Assert.All(
            openingEntries,
            t => Assert.Equal(1000m, t.Amount));
    }
}