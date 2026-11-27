using ATM.Application.Interfaces.Persistence;
using ATM.Domain.Entities;
using ATM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ATM.Infrastructure.Persistence;

public sealed class DatabaseInitializer
{
    private readonly AtmDbContext _context;
    private readonly ILedgerReconciliationService _reconciliationService;

    public DatabaseInitializer(AtmDbContext context, ILedgerReconciliationService reconciliationService)
    {
        _context = context;
        _reconciliationService = reconciliationService;
    }

    // Compatibility for existing integration tests that construct the initializer directly.
    // Both paths use the real reconciliation service, not a bypass.
    public DatabaseInitializer(AtmDbContext context)
        : this(context, new LedgerReconciliationService(context)) { }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _context.Database.MigrateAsync(cancellationToken);
        await ValidateDatabaseConfigurationAsync(cancellationToken);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var accountNames = await _context.Accounts.Select(account => account.Name).ToListAsync(cancellationToken);
        if (!accountNames.Contains("Checking")) CreateAccountWithOpeningBalance("Checking", 1000m);
        if (!accountNames.Contains("Savings")) CreateAccountWithOpeningBalance("Savings", 1000m);

        await _context.SaveChangesAsync(cancellationToken);
        // Same transaction/snapshot, including newly seeded rows. No automatic repair of historical data.
        var discrepancies = await _reconciliationService.GetDiscrepanciesAsync(cancellationToken);
        var integrityIssues = await _reconciliationService.GetIntegrityIssuesAsync(cancellationToken);
        if (discrepancies.Count != 0 || integrityIssues.Count != 0)
            throw new InvalidOperationException($"Financial ledger inconsistencies detected: {discrepancies.Count} balance mismatch(es), {integrityIssues.Count} integrity issue(s).");

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ValidateDatabaseConfigurationAsync(CancellationToken cancellationToken)
    {
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = _context.Database.GetDbConnection().CreateCommand();
            async Task<object?> Scalar(string sql)
            {
                command.CommandText = sql;
                return await command.ExecuteScalarAsync(cancellationToken);
            }
            // Also configure direct-construction callers, before starting any transaction.
            command.CommandText = "PRAGMA synchronous = FULL; PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            var journalMode = Convert.ToString(await Scalar("PRAGMA journal_mode = WAL;"));
            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A file-backed SQLite database with WAL is required.");
            if (Convert.ToInt32(await Scalar("PRAGMA synchronous;")) != 2)
                throw new InvalidOperationException("SQLite synchronous must be FULL.");
            if (Convert.ToInt32(await Scalar("PRAGMA foreign_keys;")) != 1)
                throw new InvalidOperationException("SQLite foreign keys must be enabled.");
            if (await Scalar("PRAGMA foreign_key_check;") is not null)
                throw new InvalidOperationException("SQLite foreign key violations detected.");
            var triggerCount = Convert.ToInt32(await Scalar("""
                SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'Transactions'
                    AND name IN ('TR_Transactions_PreventUpdate', 'TR_Transactions_PreventDelete', 'TR_Transactions_PreventReplace');
                """));
            if (triggerCount != 3)
                throw new InvalidOperationException("Append-Only Ledger triggers are missing. Apply the restore migration before starting the API.");
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private void CreateAccountWithOpeningBalance(string name, decimal initialBalance)
    {
        var account = Account.Create(name, initialBalance);
        var openingTransaction = Transaction.Create(account.Id, TransactionType.OpeningBalance,
            initialBalance, account.Balance);
        _context.Accounts.Add(account);
        _context.Transactions.Add(openingTransaction);
    }
}
