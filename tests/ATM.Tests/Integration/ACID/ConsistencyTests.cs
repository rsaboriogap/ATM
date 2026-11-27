using ATM.Application.DTOs;
using ATM.Domain.Entities;
using ATM.Domain.Enums;
using ATM.Domain.Exceptions;
using ATM.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ATM.Tests.Integration.ACID;

public sealed class ConsistencyTests : AcidTestBase
{
    [Fact]
    public async Task AllOperations_ShouldReconcile()
    {
        await Run(s => s.DepositAsync(Checking, new DepositRequest(500.25m)));
        await Run(s => s.WithdrawAsync(Checking, new WithdrawalRequest(200.10m)));
        await Run(s => s.TransferAsync(new TransferRequest(Checking, Savings, 300.15m)));
        var state = await ReadState();
        Assert.Equal(1000m, state.Accounts.Single(a => a.Id == Checking).Balance);
        Assert.Equal(1300.15m, state.Accounts.Single(a => a.Id == Savings).Balance);
        Assert.Equal(6, state.Entries.Count);
        AssertReconciled(state);
    }

    [Fact]
    public async Task Initialization_ShouldBeIdempotent()
    {
        await using var scope = Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        var state = await ReadState();
        Assert.Equal(2, state.Accounts.Count);
        Assert.Equal(2, state.Entries.Count);
        AssertReconciled(state);
    }

    [Theory]
    [InlineData("UPDATE Transactions SET Amount = Amount + 1", "cannot be updated")]
    [InlineData("DELETE FROM Transactions", "cannot be deleted")]
    [InlineData("INSERT OR REPLACE INTO Transactions (Id, AccountId, Type, Amount, BalanceAfter, ReferenceId, CreatedAt) SELECT Id, AccountId, Type, Amount, BalanceAfter, ReferenceId, CreatedAt FROM Transactions LIMIT 1", "cannot be replaced")]
    [InlineData("INSERT OR REPLACE INTO Transactions (Id, AccountId, Type, Amount, BalanceAfter, ReferenceId, CreatedAt) SELECT lower(hex(randomblob(16))), AccountId, Type, Amount, BalanceAfter, ReferenceId, CreatedAt FROM Transactions LIMIT 1", "cannot be replaced")]
    [InlineData("INSERT INTO Transactions (Id, AccountId, Type, Amount, BalanceAfter, ReferenceId, CreatedAt) SELECT lower(hex(randomblob(16))), AccountId, Type, Amount, BalanceAfter, ReferenceId, CreatedAt FROM Transactions LIMIT 1", "cannot be replaced")]
    public async Task Ledger_ShouldRejectDirectMutation(string sql, string message)
    {
        var before = await ReadState();
        var ex = await Assert.ThrowsAsync<SqliteException>(() => Sql(sql));
        Assert.Equal(19, ex.SqliteErrorCode);
        Assert.Contains(message, ex.Message);
        var after = await ReadState();
        Assert.Equal(before.Entries.Select(t => (t.Id,t.AccountId,t.Type,t.Amount,t.BalanceAfter,t.ReferenceId,t.CreatedAt)).OrderBy(t => t.Id),
                     after.Entries.Select(t => (t.Id,t.AccountId,t.Type,t.Amount,t.BalanceAfter,t.ReferenceId,t.CreatedAt)).OrderBy(t => t.Id));
        AssertReconciled(after);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TrackedLedgerMutation_ShouldFail(bool delete)
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AtmDbContext>();
        var entry = await db.Transactions.FirstAsync();
        if (delete) db.Transactions.Remove(entry);
        else db.Entry(entry).Property(t => t.Amount).CurrentValue = entry.Amount + 1m;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("immutable", ex.Message);
        AssertReconciled(await ReadState());
    }

    [Theory]
    [InlineData("UPDATE Accounts SET Balance = -1")]
    [InlineData("DELETE FROM Accounts")]
    public async Task Database_ShouldRejectInvalidAccountChanges(string sql)
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(() => Sql(sql));
        Assert.Equal(19, ex.SqliteErrorCode);
        AssertReconciled(await ReadState());
    }

    [Fact]
    public async Task Domain_ShouldRejectFractionalCent()
    {
        await Assert.ThrowsAsync<InvalidAmountException>(() => Run(s => s.DepositAsync(Checking, new DepositRequest(0.015m))));
        AssertReconciled(await ReadState());
    }

    [Fact]
    public async Task Reconciliation_ShouldDetectBalanceMismatch()
    {
        await Sql("UPDATE Accounts SET Balance = Balance + 1 WHERE Name = 'Checking'");
        var state = await ReadState();
        Assert.Equal(1000.01m, state.Accounts.Single(a => a.Id == Checking).Balance);
        Assert.Equal(1000m, state.Entries.Where(t => t.AccountId == Checking).Sum(t => t.Amount));
    }

    // These acceptance tests deliberately expose gaps in src(4), not skipped tests.
    [Theory, Trait("Stage", "Hardening")]
    [InlineData("UPDATE Accounts SET Balance = 100000.5")]
    [InlineData("INSERT INTO Transactions (Id, AccountId, Type, Amount, BalanceAfter, ReferenceId, CreatedAt) SELECT lower(hex(randomblob(16))), AccountId, 1, 1.5, BalanceAfter, NULL, CreatedAt FROM Transactions LIMIT 1")]
    [InlineData("INSERT INTO Transactions (Id, AccountId, Type, Amount, BalanceAfter, ReferenceId, CreatedAt) SELECT lower(hex(randomblob(16))), AccountId, 1, 1, 100001.5, NULL, CreatedAt FROM Transactions LIMIT 1")]
    public async Task Database_ShouldRejectFractionalCents(string sql)
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(() => Sql(sql));
        Assert.Equal(19, ex.SqliteErrorCode);
    }

    [Fact, Trait("Stage", "Hardening")]
    public void OpeningBalance_ShouldRejectInvalidBalanceAfter()
    {
        Assert.Throws<ArgumentException>(() => Transaction.Create(Checking, TransactionType.OpeningBalance, 1000m, 500m));
    }
}
