using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Domain.Exceptions;
using ATM.Infrastructure.Persistence;
using ATM.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ATM.Tests.Integration.Persistence;

public sealed class UnitOfWorkTests
{
    [Fact]
    public async Task Execute_ValidOperation_CommitsChanges()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var accounts = new AccountRepository(context);
        var transactions = new TransactionRepository(context);
        var unitOfWork = new EfUnitOfWork(context);

        var accountId = (await accounts.GetAllAsync())
            .First().Id;

        var balance = await unitOfWork
            .ExecuteInTransactionAsync(async ct =>
            {
                var account =
                    await accounts.GetForUpdateAsync(accountId, ct);

                account!.Deposit(150m);

                transactions.Add(
                    Transaction.Create(
                        account.Id,
                        TransactionType.Deposit,
                        150m,
                        account.Balance));

                return account.Balance;
            });

        await using var verification =
            database.CreateContext();

        var savedAccount = await verification.Accounts
            .SingleAsync(a => a.Id == accountId);

        Assert.Equal(1150m, balance);
        Assert.Equal(1150m, savedAccount.Balance);

        Assert.Equal(
            1,
            await verification.Transactions.CountAsync());
    }

    [Fact]
    public async Task Execute_DomainFailure_RollsBackChanges()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var accounts = new AccountRepository(context);
        var transactions = new TransactionRepository(context);
        var unitOfWork = new EfUnitOfWork(context);

        var accountId = (await accounts.GetAllAsync())
            .First().Id;

        await Assert.ThrowsAsync<InsufficientFundsException>(
            () => unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    var account =
                        await accounts.GetForUpdateAsync(accountId, ct);

                    account!.Deposit(100m);

                    transactions.Add(
                        Transaction.Create(
                            account.Id,
                            TransactionType.Deposit,
                            100m,
                            account.Balance));

                    // Force a domain failure.
                    account.Withdraw(5000m);

                    return account.Balance;
                }));

        await using var verification =
            database.CreateContext();

        var savedAccount = await verification.Accounts
            .SingleAsync(a => a.Id == accountId);

        Assert.Equal(1000m, savedAccount.Balance);

        Assert.Equal(
            0,
            await verification.Transactions.CountAsync());

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Execute_PersistenceFailure_RollsBackChanges()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var accounts = new AccountRepository(context);
        var transactions = new TransactionRepository(context);
        var unitOfWork = new EfUnitOfWork(context);

        var accountId = (await accounts.GetAllAsync())
            .First().Id;

        await Assert.ThrowsAsync<DbUpdateException>(
            () => unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    var account =
                        await accounts.GetForUpdateAsync(accountId, ct);

                    account!.Deposit(100m);

                    // Force a foreign-key violation.
                    transactions.Add(
                        Transaction.Create(
                            Guid.NewGuid(),
                            TransactionType.Deposit,
                            100m,
                            1100m));

                    return account.Balance;
                }));

        await using var verification =
            database.CreateContext();

        var savedAccount = await verification.Accounts
            .SingleAsync(a => a.Id == accountId);

        Assert.Equal(1000m, savedAccount.Balance);

        Assert.Equal(
            0,
            await verification.Transactions.CountAsync());

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Execute_Transfer_CommitsBothAccounts()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var accounts = new AccountRepository(context);
        var transactions = new TransactionRepository(context);
        var unitOfWork = new EfUnitOfWork(context);

        var accountList = await accounts.GetAllAsync();

        var sourceId = accountList
            .Single(a => a.Name == "Checking").Id;

        var destinationId = accountList
            .Single(a => a.Name == "Savings").Id;

        var referenceId = await unitOfWork
            .ExecuteInTransactionAsync(async ct =>
            {
                var source = await accounts
                    .GetForUpdateAsync(sourceId, ct);

                var destination = await accounts
                    .GetForUpdateAsync(destinationId, ct);

                source!.Withdraw(150m);
                destination!.Deposit(150m);

                var reference = Guid.NewGuid();

                transactions.Add(
                    Transaction.Create(
                        source.Id,
                        TransactionType.TransferOut,
                        150m,
                        source.Balance,
                        reference));

                transactions.Add(
                    Transaction.Create(
                        destination.Id,
                        TransactionType.TransferIn,
                        150m,
                        destination.Balance,
                        reference));

                return reference;
            });

        await using var verification =
            database.CreateContext();

        var savedAccounts =
            await verification.Accounts.ToListAsync();

        Assert.Equal(
            850m,
            savedAccounts.Single(a => a.Id == sourceId).Balance);

        Assert.Equal(
            1150m,
            savedAccounts.Single(a => a.Id == destinationId).Balance);

        Assert.Equal(
            2000m,
            savedAccounts.Sum(a => a.Balance));

        var history = await verification.Transactions
            .Where(t => t.ReferenceId == referenceId)
            .ToListAsync();

        Assert.Equal(2, history.Count);

        Assert.Contains(history,
            t => t.Type == TransactionType.TransferOut);

        Assert.Contains(history,
            t => t.Type == TransactionType.TransferIn);
    }

    [Fact]
    public async Task Execute_NestedTransaction_IsRejected()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var unitOfWork = new EfUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.ExecuteInTransactionAsync(
                ct => unitOfWork.ExecuteInTransactionAsync(
                    _ => Task.FromResult(true),
                    ct)));
    }
}