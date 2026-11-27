using ATM.Application.DTOs;
using ATM.Application.Exceptions;
using ATM.Domain.Enums;
using ATM.Domain.Exceptions;
using ATM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ATM.Tests.Integration.ACID;

// A retried write must post at most once, and the idempotency record must
// commit or roll back together with the ledger entries it describes.
public sealed class IdempotencyTests : AcidTestBase
{
    [Fact]
    public async Task Deposit_SameKeyTwice_PostsOnceAndReturnsOriginalResult()
    {
        var key = Guid.NewGuid();

        var first = await Run(s => s.DepositAsync(Checking, 100m, key));
        var second = await Run(s => s.DepositAsync(Checking, 100m, key));

        Assert.Equal(first, second);
        Assert.Equal(1100m, second.Account.Balance);

        var state = await ReadState();
        Assert.Equal(1100m, state.Accounts.Single(a => a.Id == Checking).Balance);
        Assert.Single(state.Entries, t => t.Type == TransactionType.Deposit);
        Assert.Equal(1, await CountIdempotencyRecords());
        AssertReconciled(state);
    }

    [Fact]
    public async Task Withdrawal_SameKeyTwice_PostsOnce()
    {
        var key = Guid.NewGuid();

        await Run(s => s.WithdrawAsync(Checking, 250m, key));
        var replay = await Run(s => s.WithdrawAsync(Checking, 250m, key));

        Assert.Equal(750m, replay.Account.Balance);

        var state = await ReadState();
        Assert.Equal(750m, state.Accounts.Single(a => a.Id == Checking).Balance);
        Assert.Single(state.Entries, t => t.Type == TransactionType.Withdrawal);
        AssertReconciled(state);
    }

    [Fact]
    public async Task Transfer_SameKeyTwice_PostsOnePair()
    {
        var key = Guid.NewGuid();
        var request = new TransferRequest(Checking, Savings, 300m);

        var first = await Run(s => s.TransferAsync(request, key));
        var second = await Run(s => s.TransferAsync(request, key));

        Assert.Equal(first, second);

        var state = await ReadState();
        Assert.Equal(700m, state.Accounts.Single(a => a.Id == Checking).Balance);
        Assert.Equal(1300m, state.Accounts.Single(a => a.Id == Savings).Balance);
        Assert.Equal(4, state.Entries.Count);
        AssertReconciled(state);
    }

    [Fact]
    public async Task SameKey_EquivalentAmountWithDifferentScale_IsReplay()
    {
        var key = Guid.NewGuid();

        await Run(s => s.DepositAsync(Checking, 100m, key));
        await Run(s => s.DepositAsync(Checking, 100.00m, key));

        var state = await ReadState();
        Assert.Single(state.Entries, t => t.Type == TransactionType.Deposit);
    }

    [Fact]
    public async Task SameKey_DifferentRequest_ThrowsConflictAndChangesNothing()
    {
        var key = Guid.NewGuid();

        await Run(s => s.DepositAsync(Checking, 100m, key));

        await Assert.ThrowsAsync<IdempotencyKeyConflictException>(
            () => Run(s => s.DepositAsync(Checking, 200m, key)));

        await Assert.ThrowsAsync<IdempotencyKeyConflictException>(
            () => Run(s => s.WithdrawAsync(Checking, 100m, key)));

        await Assert.ThrowsAsync<IdempotencyKeyConflictException>(
            () => Run(s => s.DepositAsync(Savings, 100m, key)));

        var state = await ReadState();
        Assert.Equal(1100m, state.Accounts.Single(a => a.Id == Checking).Balance);
        Assert.Equal(1000m, state.Accounts.Single(a => a.Id == Savings).Balance);
        Assert.Equal(3, state.Entries.Count);
        AssertReconciled(state);
    }

    [Fact]
    public async Task EmptyKey_IsRejectedBeforeAnyWrite()
    {
        await Assert.ThrowsAsync<IdempotencyKeyRequiredException>(
            () => Run(s => s.DepositAsync(Checking, 100m, Guid.Empty)));

        var state = await ReadState();
        Assert.Equal(2, state.Entries.Count);
        Assert.Equal(0, await CountIdempotencyRecords());
    }

    [Fact]
    public async Task BusinessFailure_DoesNotStoreKey_SoRetryRunsAgain()
    {
        var key = Guid.NewGuid();

        await Assert.ThrowsAsync<InsufficientFundsException>(
            () => Run(s => s.WithdrawAsync(Checking, 1500m, key)));

        Assert.Equal(0, await CountIdempotencyRecords());

        // Funds arrive; the retry with the same key now succeeds.
        await Run(s => s.DepositAsync(Checking, 1000m));
        var retry = await Run(s => s.WithdrawAsync(Checking, 1500m, key));

        Assert.Equal(500m, retry.Account.Balance);

        // One record for the deposit, one for the successful withdrawal.
        Assert.Equal(2, await CountIdempotencyRecords());
    }

    [Fact]
    public async Task LedgerInsertFailure_RollsBackIdempotencyRecord()
    {
        await Sql("""
            CREATE TRIGGER Test_FailDeposit BEFORE INSERT ON Transactions
            WHEN NEW.Type = 1
            BEGIN SELECT RAISE(ABORT, 'TEST_LEDGER_INSERT'); END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => Run(s => s.DepositAsync(Checking, 100m, Guid.NewGuid())));

        Assert.Equal(0, await CountIdempotencyRecords());

        var state = await ReadState();
        Assert.Equal(2, state.Entries.Count);
        AssertReconciled(state);
    }

    [Fact]
    public async Task ConcurrentRequests_SameKey_PostOnce()
    {
        var key = Guid.NewGuid();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => Task.Run(
                () => Run(s => s.DepositAsync(Checking, 100m, key)))));

        Assert.All(results, r => Assert.Equal(results[0], r));

        var state = await ReadState();
        Assert.Equal(1100m, state.Accounts.Single(a => a.Id == Checking).Balance);
        Assert.Single(state.Entries, t => t.Type == TransactionType.Deposit);
        Assert.Equal(1, await CountIdempotencyRecords());
        AssertReconciled(state);
    }

    private async Task<int> CountIdempotencyRecords()
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        return await db.IdempotencyRecords.CountAsync();
    }
}
