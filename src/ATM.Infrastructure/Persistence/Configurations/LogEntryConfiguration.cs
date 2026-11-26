using ATM.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ATM.Infrastructure.Persistence.Configurations;

public sealed class LogEntryConfiguration
    : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> builder)
    {
        builder.ToTable("Logs");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.Id)
            .ValueGeneratedOnAdd();

        builder.Property(log => log.TimestampUtc)
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();

        builder.Property(log => log.Level)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(log => log.Category)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(log => log.Message)
            .IsRequired();

        builder.Property(log => log.TraceId)
            .HasMaxLength(128);

        builder.HasIndex(log => log.TimestampUtc);

        builder.HasIndex(log => new { log.Level, log.TimestampUtc });

        builder.HasIndex(log => log.TraceId);
    }
}
