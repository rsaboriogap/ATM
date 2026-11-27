
using ATM.Domain.Entities;
using ATM.Domain.Rules;
using ATM.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ATM.Infrastructure.Persistence.Configurations;

public sealed class AccountConfiguration
    : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts", table =>
        {
            table.HasCheckConstraint(
                "CK_Accounts_Balance",
                "typeof(\"Balance\") = 'integer' AND \"Balance\" BETWEEN 0 AND 100000000000");

            table.HasCheckConstraint(
                "CK_Accounts_Currency",
                "\"Currency\" = 'USD'");
        });

        builder.HasKey(account => account.Id);

        builder.Property(account => account.Id)
            .ValueGeneratedNever();

        builder.Property(account => account.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(account => account.Balance)
            .HasConversion<MoneyValueConverter>()
            .HasColumnType("INTEGER")
            .IsRequired();

        builder.Property(account => account.Currency)
            .HasMaxLength(3)
            .IsRequired()
            .HasDefaultValue(MoneyRules.Currency);

        builder.Property(account => account.CreatedAt)
            .HasConversion(
                value => value.UtcDateTime,
                value => new DateTimeOffset(
                    DateTime.SpecifyKind(value, DateTimeKind.Utc)))
            .IsRequired();

        builder.HasIndex(account => account.Name)
            .IsUnique();
    }
}