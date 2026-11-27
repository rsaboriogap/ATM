using ATM.Domain.Entities;
using ATM.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ATM.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration
    : IEntityTypeConfiguration<Transaction>
{
    public void Configure(
        EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions", table =>
        {
            table.HasCheckConstraint(
                "CK_Transactions_Amount",
                "typeof(\"Amount\") = 'integer' AND \"Amount\" BETWEEN 1 AND 100000000");

            table.HasCheckConstraint(
                "CK_Transactions_BalanceAfter",
                "typeof(\"BalanceAfter\") = 'integer' AND \"BalanceAfter\" BETWEEN 0 AND 100000000000");

            table.HasCheckConstraint(
                "CK_Transactions_Type",
                "\"Type\" IN (1, 2, 3, 4, 5)");

            table.HasCheckConstraint(
                "CK_Transactions_ReferenceId",
                "(\r\n    (\"Type\" IN (3, 4)\r\n        AND \"ReferenceId\" IS NOT NULL)\r\n    OR\r\n    (\"Type\" IN (1, 2, 5)\r\n        AND \"ReferenceId\" IS NULL)\r\n)");

            table.HasCheckConstraint(
                "CK_Transactions_OpeningBalance",
                "\"Type\" <> 5 OR \"Amount\" = \"BalanceAfter\"");
        });

        builder.HasKey(transaction => transaction.Id);

        builder.Property(transaction => transaction.Id)
            .ValueGeneratedNever();

        builder.Property(transaction => transaction.AccountId)
            .IsRequired();

        builder.Property(transaction => transaction.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(transaction => transaction.Amount)
            .HasConversion<MoneyValueConverter>()
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(transaction => transaction.BalanceAfter)
            .HasConversion<MoneyValueConverter>()
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(transaction => transaction.ReferenceId)
            .IsRequired(false);

        builder.Property(transaction => transaction.CreatedAt)
            .HasConversion(
                value => value.UtcDateTime,
                value => new DateTimeOffset(
                    DateTime.SpecifyKind(value, DateTimeKind.Utc)))
            .IsRequired();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(transaction => transaction.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(transaction => new
        {
            transaction.AccountId,
            transaction.CreatedAt
        });

        builder.HasIndex(transaction => transaction.ReferenceId);

        builder.HasIndex(transaction => transaction.AccountId)
        .IsUnique()
        .HasFilter("\"Type\" = 5")
        .HasDatabaseName("UX_Transactions_OpeningBalance");
    }
}
