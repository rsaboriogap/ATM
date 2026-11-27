using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ATM.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAppendOnlyLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_ReferenceId",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Type",
                table: "Transactions");

            migrationBuilder.CreateIndex(
                name: "UX_Transactions_OpeningBalance",
                table: "Transactions",
                column: "AccountId",
                unique: true,
                filter: "\"Type\" = 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_OpeningBalance",
                table: "Transactions",
                sql: "\"Type\" <> 5 OR \"Amount\" = \"BalanceAfter\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_ReferenceId",
                table: "Transactions",
                sql: "(\r\n    (\"Type\" IN (3, 4)\r\n        AND \"ReferenceId\" IS NOT NULL)\r\n    OR\r\n    (\"Type\" IN (1, 2, 5)\r\n        AND \"ReferenceId\" IS NULL)\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Type",
                table: "Transactions",
                sql: "\"Type\" IN (1, 2, 3, 4, 5)");

            // NOTE: The append-only triggers live in the AddLedgerTriggers migration.
            // The check-constraint changes above force a SQLite table rebuild, which
            // EF Core runs at the end of this migration and which drops any triggers
            // created here.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Transactions_OpeningBalance",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_OpeningBalance",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_ReferenceId",
                table: "Transactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_Type",
                table: "Transactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_ReferenceId",
                table: "Transactions",
                sql: "(\r\n    (\"Type\" IN (3, 4)\r\n        AND \"ReferenceId\" IS NOT NULL)\r\n    OR\r\n    (\"Type\" IN (1, 2)\r\n        AND \"ReferenceId\" IS NULL)\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_Type",
                table: "Transactions",
                sql: "\"Type\" IN (1, 2, 3, 4)");
        }
    }
}
