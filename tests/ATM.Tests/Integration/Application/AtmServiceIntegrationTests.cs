using ATM.Application.DTOs;
using ATM.Application.Services;
using ATM.Domain.Exceptions;
using ATM.Infrastructure.Persistence;
using ATM.Infrastructure.Repositories;
using ATM.Tests.Integration.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ATM.Tests.Integration.Application;

public sealed class AtmServiceIntegrationTests
{
    private static AtmService CreateService(
        AtmDbContext context)
    {
        return new AtmService(
            new AccountRepository(context),
            new TransactionRepository(context),
            new EfUnitOfWork(context));
    }

    [Fact]
    public async Task Deposit_PersistsBalanceAndHistory()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var accounts = await service.GetAccountsAsync();

        var checking = accounts.Single(
            a => a.Name == "Checking");

        await service.DepositAsync(
            checking.Id,
            new DepositRequest(150m));

        await using var verification =
            database.CreateContext();

        var verificationService = CreateService(verification);

        var account = await verificationService
            .GetAccountAsync(checking.Id);

        var history = await verificationService
            .GetTransactionHistoryAsync(checking.Id);

        Assert.Equal(1150m, account.Balance);
        Assert.Single(history);
        Assert.Equal("Deposit", history[0].Type);
        Assert.Equal(1150m, history[0].BalanceAfter);
    }

    [Fact]
    public async Task Withdraw_PersistsBalanceAndHistory()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var checking = (await service.GetAccountsAsync())
            .Single(a => a.Name == "Checking");

        await service.WithdrawAsync(
            checking.Id,
            new WithdrawalRequest(250m));

        await using var verification =
            database.CreateContext();

        var verificationService = CreateService(verification);

        var account = await verificationService
            .GetAccountAsync(checking.Id);

        var history = await verificationService
            .GetTransactionHistoryAsync(checking.Id);

        Assert.Equal(750m, account.Balance);
        Assert.Single(history);
        Assert.Equal("Withdrawal", history[0].Type);
    }

    [Fact]
    public async Task Transfer_PersistsBothAccountsAndHistory()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var accounts = await service.GetAccountsAsync();

        var checking = accounts.Single(
            a => a.Name == "Checking");

        var savings = accounts.Single(
            a => a.Name == "Savings");

        var result = await service.TransferAsync(
            new TransferRequest(
                checking.Id,
                savings.Id,
                300m));

        await using var verification =
            database.CreateContext();

        var savedAccounts =
            await verification.Accounts.ToListAsync();

        Assert.Equal(
            700m,
            savedAccounts.Single(a => a.Id == checking.Id).Balance);

        Assert.Equal(
            1300m,
            savedAccounts.Single(a => a.Id == savings.Id).Balance);

        Assert.Equal(
            2000m,
            savedAccounts.Sum(a => a.Balance));

        var transactions = await verification.Transactions
            .Where(t => t.ReferenceId == result.ReferenceId)
            .ToListAsync();

        Assert.Equal(2, transactions.Count);
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_PersistsNothing()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var accounts = await service.GetAccountsAsync();

        await Assert.ThrowsAsync<InsufficientFundsException>(
            () => service.TransferAsync(
                new TransferRequest(
                    accounts[0].Id,
                    accounts[1].Id,
                    1500m)));

        await using var verification =
            database.CreateContext();

        var savedAccounts =
            await verification.Accounts.ToListAsync();

        Assert.All(
            savedAccounts,
            account => Assert.Equal(1000m, account.Balance));

        Assert.Empty(
            await verification.Transactions.ToListAsync());
    }

    [Fact]
    public async Task ReadBeforeWrite_DoesNotCauseTrackingConflict()
    {
        await using var database = new SqliteTestDatabase();

        await database.InitializeAsync();

        await using var context = database.CreateContext();

        var service = CreateService(context);

        var accounts = await service.GetAccountsAsync();

        var account = await service.GetAccountAsync(
            accounts[0].Id);

        var result = await service.DepositAsync(
            account.Id,
            new DepositRequest(100m));

        Assert.Equal(1100m, result.Account.Balance);
    }
}