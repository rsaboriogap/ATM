using ATM.Application.DTOs;
using ATM.Domain.Exceptions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ATM.Tests.Integration.ACID;

public sealed class IsolationTests : AcidTestBase
{
    [Fact]
    public async Task ConcurrentWithdrawals_ShouldPreventOverdraft()
    {
        using var gate = new Barrier(2);
        Task<bool> Withdraw() => Task.Run(async () =>
        {
            Assert.True(gate.SignalAndWait(TimeSpan.FromSeconds(10)));
            try { await Run(s => s.WithdrawAsync(Checking, new WithdrawalRequest(800m))); return true; }
            catch (InsufficientFundsException) { return false; }
        });
        var results = await Task.WhenAll(Withdraw(), Withdraw());
        Assert.Single(results.Where(success => success));
        var state = await ReadState();
        Assert.Equal(200m, state.Accounts.Single(a => a.Id == Checking).Balance);
        Assert.Equal(3, state.Entries.Count);
        AssertReconciled(state);
    }

    [Fact]
    public async Task ConcurrentTransfers_ShouldPreventLostUpdates()
    {
        using var gate = new Barrier(2);
        Task Transfer() => Task.Run(async () =>
        {
            Assert.True(gate.SignalAndWait(TimeSpan.FromSeconds(10)));
            await Run(s => s.TransferAsync(new TransferRequest(Checking, Savings, 300m)));
        });
        await Task.WhenAll(Transfer(), Transfer());
        var state = await ReadState();
        Assert.Equal(400m, state.Accounts.Single(a => a.Id == Checking).Balance);
        Assert.Equal(1600m, state.Accounts.Single(a => a.Id == Savings).Balance);
        Assert.Equal(6, state.Entries.Count);
        AssertReconciled(state);
    }

    [Fact]
    public async Task UncommittedChanges_ShouldNotBeVisible_AndSecondWriterShouldBeBlocked()
    {
        await using var a = new SqliteConnection(ConnectionString);
        await using var b = new SqliteConnection(new SqliteConnectionStringBuilder(ConnectionString) { DefaultTimeout = 1 }.ToString());
        await a.OpenAsync(); await b.OpenAsync();
        await using var tx = a.BeginTransaction(deferred: false);
        await using var change = a.CreateCommand();
        change.Transaction = tx;
        change.CommandText = "UPDATE Accounts SET Balance = Balance + 100 WHERE Name = 'Checking'";
        await change.ExecuteNonQueryAsync();
        await using var read = b.CreateCommand();
        read.CommandText = "SELECT Balance FROM Accounts WHERE Name = 'Checking'";
        Assert.Equal(100000L, Convert.ToInt64(await read.ExecuteScalarAsync()));
        var ex = Assert.Throws<SqliteException>(() => { using var blocked = b.BeginTransaction(deferred: false); });
        Assert.Contains(ex.SqliteErrorCode, new[] { 5, 6 });
        await tx.RollbackAsync();
        Assert.Equal(100000L, Convert.ToInt64(await read.ExecuteScalarAsync()));
    }
}
