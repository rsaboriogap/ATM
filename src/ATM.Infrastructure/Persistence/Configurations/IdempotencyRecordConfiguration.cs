using ATM.Application.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ATM.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRecordConfiguration
    : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(
        EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.HasKey(record => record.Key);

        builder.Property(record => record.Key)
            .ValueGeneratedNever();

        builder.Property(record => record.Fingerprint)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(record => record.ResponseJson)
            .IsRequired();

        builder.Property(record => record.CreatedAt)
            .IsRequired();
    }
}
