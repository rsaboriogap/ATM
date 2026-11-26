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

        repository.Add(
            Transaction.Create(
                account.Id,
                TransactionType.Deposit,
                100m,
                1100m));

        await using var verificationContext =
            database.CreateContext();

        Assert.Equal(
            0,
            await verificationContext.Transactions.CountAsync());

        await context.SaveChangesAsync();

        Assert.Equal(
            1,
            await verificationContext.Transactions.CountAsync());
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
            account.Deposit(100m);

            repository.Add(
                Transaction.Create(
                    account.Id,
                    TransactionType.Deposit,
                    100m,
                    account.Balance));
        }

        await context.SaveChangesAsync();

        var transactions =
            await repository.GetByAccountIdAsync(
                accounts[0].Id);

        Assert.Single(transactions);

        Assert.Equal(
            accounts[0].Id,
            transactions[0].AccountId);
    }
}