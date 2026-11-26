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

        var account = Account.Create("Checking", 1250.75m);

        context.Accounts.Add(account);

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

        context.Accounts.Add(
            Account.Create("Checking", 1250.75m));

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

        var account = Account.Create("Checking", 1000m);

        account.Deposit(150m);

        var transaction = Transaction.Create(
            account.Id,
            TransactionType.Deposit,
            150m,
            account.Balance);

        context.Accounts.Add(account);
        context.Transactions.Add(transaction);

        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        var savedTransaction = await context.Transactions
            .SingleAsync();

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
            Account.Create("Checking", 1000m));

        await context.SaveChangesAsync();

        context.Accounts.Add(
            Account.Create("Checking", 500m));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task InitializeDatabase_MultipleTimes_DoesNotResetAccounts()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AtmDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AtmDbContext(options);

        var initializer = new DatabaseInitializer(context);

        await initializer.InitializeAsync();

        var account = await context.Accounts
            .SingleAsync(a => a.Name == "Checking");

        account.Deposit(250m);

        await context.SaveChangesAsync();

        await initializer.InitializeAsync();

        context.ChangeTracker.Clear();

        Assert.Equal(2, await context.Accounts.CountAsync());

        var savedAccount = await context.Accounts
            .SingleAsync(a => a.Name == "Checking");

        Assert.Equal(1250m, savedAccount.Balance);
    }
}