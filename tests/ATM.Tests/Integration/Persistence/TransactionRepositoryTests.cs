using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ATM.Tests.Integration.Persistence;

public sealed class TransactionRepositoryTests
{
    [Fact]
    public async Task Add_DoesNotPersistUntilSaveChanges()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var account = await context.Accounts
            .AsNoTracking()
            .FirstAsync();

        var repository = new TransactionRepository(context);

        repository.Add(account.Deposit(100m));

        await using var verificationContext =
            database.CreateContext();

        Assert.Equal(
            0,
            await verificationContext.ActivityTransactions().CountAsync());

        await context.SaveChangesAsync();

        Assert.Equal(
            1,
            await verificationContext.ActivityTransactions().CountAsync());
    }

    [Fact]
    public async Task GetByAccountIdAsync_FiltersTransactions()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var accounts = await context.Accounts.ToListAsync();

        var repository = new TransactionRepository(context);

        foreach (var account in accounts)
        {
            repository.Add(account.Deposit(100m));
        }

        await context.SaveChangesAsync();

        var transactions =
            await repository.GetByAccountIdAsync(
                accounts[0].Id);

        // Opening balance + the deposit above, both for this account only.
        Assert.Equal(2, transactions.Count);

        Assert.All(
            transactions,
            t => Assert.Equal(accounts[0].Id, t.AccountId));

        Assert.Single(
            transactions,
            t => t.Type == TransactionType.Deposit);
    }

    [Fact]
    public async Task GetByAccountIdAsync_OrdersByLedgerSequenceDescending()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var account = await context.Accounts
            .AsTracking()
            .FirstAsync();

        var repository = new TransactionRepository(context);

        // One entry per SaveChanges: EF does not guarantee insert order within a batch.
        repository.Add(account.Deposit(100m));
        await context.SaveChangesAsync();
        repository.Add(account.Withdraw(40m));
        await context.SaveChangesAsync();

        var transactions =
            await repository.GetByAccountIdAsync(account.Id);

        Assert.Equal(
            new long[] { 3, 2, 1 },
            transactions.Select(t => t.Sequence));

        Assert.Equal(
            new[] { TransactionType.Withdrawal, TransactionType.Deposit, TransactionType.OpeningBalance },
            transactions.Select(t => t.Type));
    }
}