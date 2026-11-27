using ATM.Application.DTOs;
using ATM.Application.Interfaces.Persistence;
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
    [InlineData("INSERT OR REPLACE INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt) SELECT Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt FROM Transactions LIMIT 1", "cannot be replaced")]
    [InlineData("INSERT OR REPLACE INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt) SELECT lower(hex(randomblob(16))), AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt FROM Transactions LIMIT 1", "cannot be replaced")]
    [InlineData("INSERT INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt) SELECT lower(hex(randomblob(16))), AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt FROM Transactions LIMIT 1", "cannot be replaced")]
    public async Task Ledger_ShouldRejectDirectMutation(string sql, string message)
    {
        var before = await ReadState();
        var ex = await Assert.ThrowsAsync<SqliteException>(() => Sql(sql));
        Assert.Equal(19, ex.SqliteErrorCode);
        Assert.Contains(message, ex.Message);
        var after = await ReadState();
        Assert.Equal(before.Entries.Select(t => (t.Id,t.AccountId,t.Sequence,t.Type,t.Amount,t.BalanceAfter,t.ReferenceId,t.CreatedAt)).OrderBy(t => t.Id),
                     after.Entries.Select(t => (t.Id,t.AccountId,t.Sequence,t.Type,t.Amount,t.BalanceAfter,t.ReferenceId,t.CreatedAt)).OrderBy(t => t.Id));
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
    public async Task Database_ShouldRejectBalanceChangeWithoutLedgerEntry()
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(
            () => Sql("UPDATE Accounts SET Balance = Balance + 1 WHERE Name = 'Checking'"));
        Assert.Equal(19, ex.SqliteErrorCode);
        Assert.Contains("only change through a ledger entry", ex.Message);
        AssertReconciled(await ReadState());
    }

    [Fact]
    public async Task Database_ShouldRejectLedgerSequenceChangeWithoutLedgerEntry()
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(
            () => Sql("UPDATE Accounts SET LedgerSequence = LedgerSequence + 1 WHERE Name = 'Checking'"));
        Assert.Equal(19, ex.SqliteErrorCode);
        Assert.Contains("only change through a ledger entry", ex.Message);
        AssertReconciled(await ReadState());
    }

    [Fact]
    public async Task LedgerInsert_ShouldDriveAccountBalance()
    {
        await Sql("""
            INSERT INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt)
            SELECT lower(hex(randomblob(16))), a.Id, a.LedgerSequence + 1, 1, 2500, a.Balance + 2500, NULL, t.CreatedAt
            FROM Accounts a JOIN Transactions t ON t.AccountId = a.Id
            WHERE a.Name = 'Checking' LIMIT 1
            """);
        var state = await ReadState();
        var checking = state.Accounts.Single(a => a.Id == Checking);
        Assert.Equal(1025m, checking.Balance);
        Assert.Equal(2, checking.LedgerSequence);
        AssertReconciled(state);
    }

    [Theory]
    [InlineData(1, 2500, 100000)]   // deposit that doesn't move the balance
    [InlineData(2, 2500, 100000)]   // withdrawal that doesn't move the balance
    [InlineData(1, 2500, 999999)]   // arbitrary BalanceAfter
    public async Task Database_ShouldRejectBrokenRunningBalance(int type, long amount, long balanceAfter)
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(() => Sql($"""
            INSERT INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt)
            SELECT lower(hex(randomblob(16))), AccountId, Sequence + 1, {type}, {amount}, {balanceAfter}, NULL, CreatedAt
            FROM Transactions LIMIT 1
            """));
        Assert.Equal(19, ex.SqliteErrorCode);
        Assert.Contains("running balance", ex.Message);
        AssertReconciled(await ReadState());
    }

    [Theory]
    [InlineData(0)]   // missing sequence (column default)
    [InlineData(1)]   // duplicate of the opening entry's position
    [InlineData(3)]   // gap
    public async Task Database_ShouldRejectOutOfOrderSequence(long sequence)
    {
        // Running balance is valid, so only the sequence rule is violated.
        var ex = await Assert.ThrowsAsync<SqliteException>(() => Sql($"""
            INSERT INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt)
            SELECT lower(hex(randomblob(16))), a.Id, {sequence}, 1, 2500, a.Balance + 2500, NULL, a.CreatedAt
            FROM Accounts a WHERE a.Name = 'Checking'
            """));
        Assert.Equal(19, ex.SqliteErrorCode);
        Assert.Contains("Sequence does not continue", ex.Message);
        AssertReconciled(await ReadState());
    }

    [Fact]
    public async Task LedgerOrder_ShouldSurviveRowidRenumbering()
    {
        await Run(s => s.DepositAsync(Checking, new DepositRequest(500m)));
        await Run(s => s.WithdrawAsync(Checking, new WithdrawalRequest(200m)));
        await Run(s => s.TransferAsync(new TransferRequest(Checking, Savings, 300m)));

        // Simulate a table rebuild / VACUUM that renumbers rowids: re-insert every
        // entry in reverse ledger order, then restore the original triggers.
        await using (var connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync();
            var triggers = new List<(string Name, string Sql)>();
            await using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'Transactions'";
                await using var reader = await read.ExecuteReaderAsync();
                while (await reader.ReadAsync()) triggers.Add((reader.GetString(0), reader.GetString(1)));
            }
            Assert.NotEmpty(triggers);

            await using var command = connection.CreateCommand();
            command.CommandText = string.Join("\n", triggers.Select(t => $"DROP TRIGGER {t.Name};")) + """

                CREATE TEMP TABLE Snapshot AS SELECT * FROM Transactions;
                DELETE FROM Transactions;
                INSERT INTO Transactions SELECT * FROM Snapshot ORDER BY Sequence DESC;
                DROP TABLE Snapshot;
                """ + "\n" + string.Join("\n", triggers.Select(t => t.Sql + ";"));
            await command.ExecuteNonQueryAsync();

            // Sanity check: rowid order now contradicts ledger order.
            command.CommandText = """
                SELECT group_concat(Sequence) FROM (
                    SELECT t.Sequence FROM Transactions t JOIN Accounts a ON a.Id = t.AccountId
                    WHERE a.Name = 'Checking' ORDER BY t.rowid)
                """;
            Assert.Equal("4,3,2,1", await command.ExecuteScalarAsync());
        }

        // Startup checks (triggers, reconciliation) still pass...
        await using (var scope = Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        }

        // ...and the ledger keeps accepting correctly ordered entries.
        var result = await Run(s => s.DepositAsync(Checking, new DepositRequest(25m)));
        Assert.Equal(1025m, result.Account.Balance);

        var state = await ReadState();
        var checkingEntries = state.Entries.Where(t => t.AccountId == Checking).OrderBy(t => t.Sequence).ToList();
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, checkingEntries.Select(t => t.Sequence));
        Assert.Equal(1025m, checkingEntries[^1].BalanceAfter);
        Assert.Equal(5, state.Accounts.Single(a => a.Id == Checking).LedgerSequence);
        AssertReconciled(state);
    }

    [Fact]
    public async Task Reconciliation_ShouldDetectBalanceMismatch_WhenGuardIsBypassed()
    {
        // Simulate tampering by someone with file access who drops the guard.
        await Sql("DROP TRIGGER TR_Accounts_BalanceFollowsLedger");
        await Sql("UPDATE Accounts SET Balance = Balance + 1 WHERE Name = 'Checking'");

        await using var scope = Provider.CreateAsyncScope();
        var reconciliation = scope.ServiceProvider.GetRequiredService<ILedgerReconciliationService>();

        var discrepancy = Assert.Single(await reconciliation.GetDiscrepanciesAsync());
        Assert.Equal(Checking, discrepancy.AccountId);

        var issues = await reconciliation.GetIntegrityIssuesAsync();
        Assert.Contains(issues, i => i.StartsWith("Stored balance differs from last ledger entry"));
    }

    [Fact]
    public async Task Reconciliation_ShouldDetectBrokenRunningBalance_WhenTriggerIsBypassed()
    {
        // Two entries that cancel out keep SUM() correct but break the chain:
        // only the running-balance check can see this.
        await Sql("DROP TRIGGER TR_Transactions_EnforceRunningBalance");
        await Sql("DROP TRIGGER TR_Accounts_BalanceFollowsLedger");
        await Sql("""
            INSERT INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt)
            SELECT lower(hex(randomblob(16))), a.Id, a.LedgerSequence + 1, 1, 5000, 500000, NULL, t.CreatedAt
            FROM Accounts a JOIN Transactions t ON t.AccountId = a.Id
            WHERE a.Name = 'Checking' LIMIT 1
            """);
        await Sql("""
            INSERT INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt)
            SELECT lower(hex(randomblob(16))), a.Id, a.LedgerSequence + 1, 2, 5000, 100000, NULL, t.CreatedAt
            FROM Accounts a JOIN Transactions t ON t.AccountId = a.Id
            WHERE a.Name = 'Checking' LIMIT 1
            """);

        await using var scope = Provider.CreateAsyncScope();
        var reconciliation = scope.ServiceProvider.GetRequiredService<ILedgerReconciliationService>();

        Assert.Empty(await reconciliation.GetDiscrepanciesAsync());

        var issues = await reconciliation.GetIntegrityIssuesAsync();
        Assert.Equal(2, issues.Count(i => i.StartsWith("Broken running balance")));
    }

    [Fact]
    public async Task Reconciliation_ShouldDetectSequenceGap_WhenTriggerIsBypassed()
    {
        await Sql("DROP TRIGGER TR_Transactions_EnforceSequence");
        await Sql("""
            INSERT INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt)
            SELECT lower(hex(randomblob(16))), a.Id, a.LedgerSequence + 2, 1, 2500, a.Balance + 2500, NULL, a.CreatedAt
            FROM Accounts a WHERE a.Name = 'Checking'
            """);

        await using var scope = Provider.CreateAsyncScope();
        var reconciliation = scope.ServiceProvider.GetRequiredService<ILedgerReconciliationService>();

        var issues = await reconciliation.GetIntegrityIssuesAsync();
        Assert.Single(issues, i => i.StartsWith("Ledger sequence gap"));
    }

    // These acceptance tests deliberately expose gaps in src(4), not skipped tests.
    [Theory, Trait("Stage", "Hardening")]
    [InlineData("UPDATE Accounts SET Balance = 100000.5")]
    [InlineData("INSERT INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt) SELECT lower(hex(randomblob(16))), AccountId, Sequence + 1, 1, 1.5, BalanceAfter, NULL, CreatedAt FROM Transactions LIMIT 1")]
    [InlineData("INSERT INTO Transactions (Id, AccountId, Sequence, Type, Amount, BalanceAfter, ReferenceId, CreatedAt) SELECT lower(hex(randomblob(16))), AccountId, Sequence + 1, 1, 1, 100001.5, NULL, CreatedAt FROM Transactions LIMIT 1")]
    public async Task Database_ShouldRejectFractionalCents(string sql)
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(() => Sql(sql));
        Assert.Equal(19, ex.SqliteErrorCode);
    }

    [Fact, Trait("Stage", "Hardening")]
    public void OpeningBalance_ShouldRejectInvalidBalanceAfter()
    {
        Assert.Throws<ArgumentException>(() => Transaction.Create(Checking, Transaction.OpeningSequence, TransactionType.OpeningBalance, 1000m, 500m));
    }
}
