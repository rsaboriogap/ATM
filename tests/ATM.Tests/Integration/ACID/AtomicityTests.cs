using ATM.Application.DTOs;
using ATM.Domain.Enums;
using ATM.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ATM.Tests.Integration.ACID;

public sealed class AtomicityTests : AcidTestBase
{
    [Fact]
    public async Task Transfer_ShouldCommitAllChanges()
    {
        await Run(s => s.TransferAsync(new TransferRequest(Checking, Savings, 300m)));
        var state = await ReadState();
        Assert.Equal(700m, state.Accounts.Single(a => a.Id == Checking).Balance);
        Assert.Equal(1300m, state.Accounts.Single(a => a.Id == Savings).Balance);
        Assert.Equal(4, state.Entries.Count);
        Assert.Equal(700m, Assert.Single(state.Entries.Where(t => t.Type == TransactionType.TransferOut)).BalanceAfter);
        Assert.Equal(1300m, Assert.Single(state.Entries.Where(t => t.Type == TransactionType.TransferIn)).BalanceAfter);
        AssertReconciled(state);
    }

    [Fact]
    public async Task Transfer_ShouldRollback_WhenSecondInsertFails()
    {
        // Independent of EF's ordering: reject whichever transfer entry arrives second.
        await Sql("""
            CREATE TRIGGER Test_FailSecondTransfer BEFORE INSERT ON Transactions
            WHEN NEW.Type IN (3,4) AND EXISTS (
                SELECT 1 FROM Transactions WHERE ReferenceId = NEW.ReferenceId)
            BEGIN SELECT RAISE(ABORT, 'TEST_SECOND_TRANSFER'); END;
            """);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Run(s => s.TransferAsync(new TransferRequest(Checking, Savings, 300m))));
        Assert.Contains("TEST_SECOND_TRANSFER", ex.ToString());
        await AssertUnchanged();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AccountOperation_ShouldRollback_WhenLedgerInsertFails(bool deposit)
    {
        await Sql("""
            CREATE TRIGGER Test_FailAccountOperation BEFORE INSERT ON Transactions
            WHEN NEW.Type IN (1,2)
            BEGIN SELECT RAISE(ABORT, 'TEST_LEDGER_INSERT'); END;
            """);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => deposit
            ? Run(s => s.DepositAsync(Checking, 100m))
            : Run(s => s.WithdrawAsync(Checking, 100m)));
        Assert.Contains("TEST_LEDGER_INSERT", ex.ToString());
        await AssertUnchanged();
    }

    [Fact]
    public async Task InsufficientFunds_ShouldLeaveStateUnchanged()
    {
        await Assert.ThrowsAsync<InsufficientFundsException>(() => Run(s => s.WithdrawAsync(Checking, 1001m)));
        await AssertUnchanged();
    }

    private async Task AssertUnchanged()
    {
        var state = await ReadState();
        Assert.Equal(2, state.Accounts.Count);
        Assert.All(state.Accounts, a => Assert.Equal(1000m, a.Balance));
        Assert.Equal(2, state.Entries.Count);
        Assert.All(state.Entries, t => Assert.Equal(TransactionType.OpeningBalance, t.Type));
        AssertReconciled(state);
    }
}
