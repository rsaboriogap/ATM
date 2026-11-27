using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ATM.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Replaces the ledger's dependency on SQLite's implicit rowid with an explicit,
    /// per-account <c>Transactions.Sequence</c> (1 = OpeningBalance, then previous + 1).
    /// </summary>
    /// <remarks>
    /// rowid is not stable for tables without an INTEGER PRIMARY KEY: VACUUM and table
    /// rebuilds may renumber it. The backfill below is the last place rowid order is read,
    /// while it still reflects insertion order. Only ADD COLUMN / CREATE INDEX are used, so
    /// no table rebuild happens here. Any future migration that rebuilds Transactions or
    /// Accounts drops the triggers and must recreate them.
    /// </remarks>
    public partial class LedgerSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lift the guards for the one-time backfill; all are recreated below.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Transactions_PreventUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Transactions_EnforceRunningBalance;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Transactions_SyncAccountBalance;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Accounts_BalanceFollowsLedger;");

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LedgerSequence",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Number existing entries per account in their current (insertion) order.
            migrationBuilder.Sql("""
                UPDATE Transactions
                SET Sequence = (
                    SELECT COUNT(*) FROM Transactions t
                    WHERE t.AccountId = Transactions.AccountId
                      AND t.rowid <= Transactions.rowid);
                """);

            migrationBuilder.Sql("""
                UPDATE Accounts
                SET LedgerSequence = COALESCE(
                    (SELECT MAX(t.Sequence) FROM Transactions t WHERE t.AccountId = Accounts.Id), 0);
                """);

            migrationBuilder.CreateIndex(
                name: "UX_Transactions_AccountSequence",
                table: "Transactions",
                columns: new[] { "AccountId", "Sequence" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Transactions_PreventUpdate
                BEFORE UPDATE ON Transactions
                BEGIN
                    SELECT RAISE(ABORT, 'Transaction records cannot be updated.');
                END;
                """);

            // OpeningBalance is sequence 1; every other entry is exactly the next sequence.
            // (A second OpeningBalance is rejected by TR_Transactions_PreventReplace.)
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Transactions_EnforceSequence
                BEFORE INSERT ON Transactions
                WHEN (NEW.Type = 5 AND NEW.Sequence IS NOT 1)
                  OR (NEW.Type <> 5 AND NEW.Sequence IS NOT (
                        SELECT MAX(t.Sequence) + 1 FROM Transactions t
                         WHERE t.AccountId = NEW.AccountId))
                BEGIN
                    SELECT RAISE(ABORT, 'Sequence does not continue the account ledger.');
                END;
                """);

            // Every non-opening entry must continue the running balance of the latest entry.
            // No previous entry => NULL => rejected, so OpeningBalance must come first.
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Transactions_EnforceRunningBalance
                BEFORE INSERT ON Transactions
                WHEN NEW.Type <> 5
                 AND NEW.BalanceAfter IS NOT (
                       (SELECT t.BalanceAfter FROM Transactions t
                         WHERE t.AccountId = NEW.AccountId
                         ORDER BY t.Sequence DESC LIMIT 1)
                       + CASE WHEN NEW.Type IN (1, 3) THEN NEW.Amount ELSE -NEW.Amount END)
                BEGIN
                    SELECT RAISE(ABORT, 'BalanceAfter does not continue the account running balance.');
                END;
                """);

            // The ledger drives Accounts.Balance and Accounts.LedgerSequence.
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Transactions_SyncAccountBalance
                AFTER INSERT ON Transactions
                BEGIN
                    UPDATE Accounts
                    SET Balance = NEW.BalanceAfter, LedgerSequence = NEW.Sequence
                    WHERE Id = NEW.AccountId;
                END;
                """);

            // Any other write to Balance or LedgerSequence (raw SQL, tools, bugs) is rejected.
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_Accounts_BalanceFollowsLedger
                BEFORE UPDATE OF Balance, LedgerSequence ON Accounts
                WHEN NEW.LedgerSequence IS NOT (
                       SELECT MAX(t.Sequence) FROM Transactions t WHERE t.AccountId = NEW.Id)
                  OR NEW.Balance IS NOT (
                       SELECT t.BalanceAfter FROM Transactions t
                        WHERE t.AccountId = NEW.Id
                        ORDER BY t.Sequence DESC LIMIT 1)
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
