using System.Net;
using System.Net.Http.Json;
using ATM.Api.ErrorHandling;
using ATM.Domain.Rules;
using ATM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ATM.Tests.Integration.Api;

public sealed class AtmFinancialBoundaryTests
    : BackendTestBase
{
    [Fact]
    public async Task Deposit_MinimumAmount_IsAccepted()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var result = await DepositAsync(
            client, accounts[0].Id, 0.01m);

        Assert.Equal(1000.01m, result.Account.Balance);
        Assert.Equal(0.01m, result.Transaction.Amount);
    }

    [Fact]
    public async Task Deposit_MaximumAmount_IsAccepted()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var result = await DepositAsync(
            client, accounts[0].Id, 1_000_000m);

        Assert.Equal(1_001_000m, result.Account.Balance);
    }

    [Fact]
    public async Task Withdraw_ExactBalance_LeavesZero()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var result = await WithdrawAsync(
            client, accounts[0].Id, accounts[0].Balance);

        Assert.Equal(0m, result.Account.Balance);

        var persisted = await GetAccountAsync(
            client, accounts[0].Id);

        Assert.Equal(0m, persisted.Balance);
    }

    [Fact]
    public async Task Withdraw_OneCentOverBalance_IsRejected()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        using var response = await client.PostAsJsonAsync(
            $"/api/accounts/{account.Id}/withdrawals",
            new { Amount = account.Balance + 0.01m });

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            ErrorCodes.InsufficientFunds);

        var persisted = await GetAccountAsync(
            client, account.Id);

        Assert.Equal(account.Balance, persisted.Balance);

        Assert.Empty(
            await GetHistoryAsync(client, account.Id));
    }

    [Fact]
    public async Task Deposit_ReachingMaximumBalance_IsAccepted()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        await SetBalanceAsync(
            factory, account.Id, 999_999_999.99m);

        var result = await DepositAsync(
            client, account.Id, 0.01m);

        Assert.Equal(
            1_000_000_000m,
            result.Account.Balance);
    }

    [Fact]
    public async Task Deposit_ExceedingMaximumBalance_IsRejected()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        await SetBalanceAsync(
            factory, account.Id, 1_000_000_000m);

        var historyBefore = await GetHistoryAsync(
            client, account.Id);

        using var response = await client.PostAsJsonAsync(
            $"/api/accounts/{account.Id}/deposits",
            new { Amount = 0.01m });

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            ErrorCodes.BalanceLimitExceeded);

        var persisted = await GetAccountAsync(
            client, account.Id);

        Assert.Equal(1_000_000_000m, persisted.Balance);

        Assert.Equal(
            historyBefore.Length,
            (await GetHistoryAsync(client, account.Id)).Length);
    }

    [Fact]
    public async Task Transfer_DestinationExceedsLimit_IsAtomic()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);

        var source = accounts[0];
        var destination = accounts[1];

        await SetBalanceAsync(
            factory, destination.Id, 1_000_000_000m);

        var destinationHistoryBefore = await GetHistoryAsync(
            client, destination.Id);

        using var response = await client.PostAsJsonAsync(
            "/api/transfers",
            new
            {
                SourceAccountId = source.Id,
                DestinationAccountId = destination.Id,
                Amount = 100m
            });

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            ErrorCodes.BalanceLimitExceeded);

        var updatedSource = await GetAccountAsync(
            client, source.Id);

        var updatedDestination = await GetAccountAsync(
            client, destination.Id);

        Assert.Equal(1000m, updatedSource.Balance);

        Assert.Equal(
            1_000_000_000m,
            updatedDestination.Balance);

        Assert.Empty(
            await GetHistoryAsync(client, source.Id));

        Assert.Equal(
            destinationHistoryBefore.Length,
            (await GetHistoryAsync(client, destination.Id)).Length);
    }

    [Fact]
    public async Task RepeatedCentDeposits_PreservePrecision()
    {
        await using var factory = new AtmApiFactory();
        using var client = CreateClient(factory);

        var accounts = await GetAccountsAsync(client);
        var account = accounts[0];

        for (var i = 0; i < 10; i++)
        {
            await DepositAsync(client, account.Id, 0.01m);
        }

        var persisted = await GetAccountAsync(
            client, account.Id);

        Assert.Equal(1000.10m, persisted.Balance);

        var history = await GetHistoryAsync(
            client, account.Id);

        Assert.Equal(10, history.Length);
        Assert.Equal(0.10m, history.Sum(t => t.Amount));
    }

    /// <summary>
    /// Raises the balance to <paramref name="balance"/> through real deposit
    /// ledger entries. Accounts.Balance can no longer be written directly:
    /// the database only lets it follow the ledger.
    /// </summary>
    private static async Task SetBalanceAsync(
        AtmApiFactory factory,
        Guid accountId,
        decimal balance)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var context = scope.ServiceProvider
            .GetRequiredService<AtmDbContext>();

        // One commit for all entries keeps the setup fast.
        await using var transaction =
            await context.Database.BeginTransactionAsync();

        var account = await context.Accounts
            .AsTracking()
            .SingleAsync(a => a.Id == accountId);

        Assert.True(balance >= account.Balance);

        while (account.Balance < balance)
        {
            var amount = Math.Min(
                MoneyRules.MaximumAmount,
                balance - account.Balance);

            // One entry per account per SaveChanges: EF does not guarantee
            // insert order within a batch, and each entry must be the next Sequence.
            context.Transactions.Add(account.Deposit(amount));

            await context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
    }
}