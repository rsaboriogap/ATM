using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ATM.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes the ledger the source of truth for Accounts.Balance and enforces
    /// running-balance continuity on every insert.
    /// </summary>
    /// <remarks>
    /// Ordering relies on SQLite's implicit rowid (insertion order). Any future migration
    /// that rebuilds Transactions or Accounts drops these triggers and must recreate them.
    /// </remarks>
    public partial class LedgerDrivesAccountBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every non-opening entry must continue the account's running balance.
            // No previous entry => NULL => rejected, so OpeningBalance must come first.
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS TR_Transactions_EnforceRunningBalance
                BEFORE INSERT ON Transactions
                WHEN NEW.Type <> 5
                 AND NEW.BalanceAfter IS NOT (
                       (SELECT t.BalanceAfter FROM Transactions t
                         WHERE t.AccountId = NEW.AccountId
                         ORDER BY t.rowid DESC LIMIT 1)
                       + CASE WHEN NEW.Type IN (1, 3) THEN NEW.Amount ELSE -NEW.Amount END)
                BEGIN
                    SELECT RAISE(ABORT, 'BalanceAfter does not continue the account running balance.');
                END;
                """);

            // The ledger drives Accounts.Balance.
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS TR_Transactions_SyncAccountBalance
                AFTER INSERT ON Transactions
                BEGIN
                    UPDATE Accounts SET Balance = NEW.BalanceAfter WHERE Id = NEW.AccountId;
                END;
                """);

            // Any other write to Balance (raw SQL, tools, bugs) is rejected.
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS TR_Accounts_BalanceFollowsLedger
                BEFORE UPDATE OF Balance ON Accounts
                WHEN NEW.Balance IS NOT (
                       SELECT t.BalanceAfter FROM Transactions t
                        WHERE t.AccountId = NEW.Id
                        ORDER BY t.rowid DESC LIMIT 1)
                BEGIN
                    SELECT RAISE(ABORT, 'Account balance can only change through a ledger entry.');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new System.NotSupportedException("Downgrading would remove financial ledger protections.");
    }
}
