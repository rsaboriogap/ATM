using Microsoft.EntityFrameworkCore.Migrations;

namespace ATM.Infrastructure.Persistence.Migrations;

public partial class EnforceIntegerMoneyStorage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_Accounts_Balance", "Accounts");
        migrationBuilder.AddCheckConstraint("CK_Accounts_Balance", "Accounts",
            "typeof(\"Balance\") = 'integer' AND \"Balance\" BETWEEN 0 AND 100000000000");
        migrationBuilder.DropCheckConstraint("CK_Transactions_Amount", "Transactions");
        migrationBuilder.AddCheckConstraint("CK_Transactions_Amount", "Transactions",
            "typeof(\"Amount\") = 'integer' AND \"Amount\" BETWEEN 1 AND 100000000");
        migrationBuilder.DropCheckConstraint("CK_Transactions_BalanceAfter", "Transactions");
        migrationBuilder.AddCheckConstraint("CK_Transactions_BalanceAfter", "Transactions",
            "typeof(\"BalanceAfter\") = 'integer' AND \"BalanceAfter\" BETWEEN 0 AND 100000000000");
    }

    // Downgrade requires a reviewed maintenance plan: rebuilding can remove ledger triggers.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Use a reviewed backup/restore plan to downgrade financial schema.");
}
