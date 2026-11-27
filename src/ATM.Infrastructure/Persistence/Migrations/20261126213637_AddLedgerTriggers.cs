using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ATM.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes the Transactions ledger append-only via SQLite triggers.
    /// </summary>
    /// <remarks>
    /// Kept in its own migration on purpose: any migration that forces a SQLite
    /// rebuild of the Transactions table (e.g. adding/dropping a check constraint,
    /// altering a column) drops these triggers. If a future migration rebuilds
    /// Transactions, it must recreate them at the end (or be followed by one that does).
    /// </remarks>
    public partial class AddLedgerTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS TR_Transactions_PreventUpdate
                BEFORE UPDATE ON Transactions
                BEGIN
                    SELECT RAISE(
                        ABORT,
                        'Transaction records cannot be updated.'
                    );
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS TR_Transactions_PreventDelete
                BEFORE DELETE ON Transactions
                BEGIN
                    SELECT RAISE(
                        ABORT,
                        'Transaction records cannot be deleted.'
                    );
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS TR_Transactions_PreventReplace
                BEFORE INSERT ON Transactions
                WHEN EXISTS (
                    SELECT 1
                    FROM Transactions
                    WHERE Id = NEW.Id
                       OR (
                           NEW.Type = 5
                           AND Type = 5
                           AND AccountId = NEW.AccountId
                       )
                )
                BEGIN
                    SELECT RAISE(
                        ABORT,
                        'Existing transaction records cannot be replaced.'
                    );
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Transactions_PreventUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Transactions_PreventDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Transactions_PreventReplace;");
        }
    }
}
