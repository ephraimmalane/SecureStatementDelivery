using Domain.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Statements;

internal sealed class StatementConfiguration : IEntityTypeConfiguration<Statement>
{
    public void Configure(EntityTypeBuilder<Statement> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.OriginalFileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(s => s.StoragePath)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(s => s.ContentType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.Period)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.Description)
            .HasMaxLength(500)
            .HasDefaultValue(string.Empty);

        builder.Property(s => s.IsPasswordProtected)
            .HasDefaultValue(false);

        builder.Property(s => s.Status)
            .IsRequired();

        builder.Property(s => s.RevokedReason)
            .HasMaxLength(500);

        builder.Property(s => s.IdempotencyKey)
            .HasMaxLength(128);

        // Unique across non-null keys only, so statements uploaded without a key are unaffected.
        // This is the hard guard that makes ingestion idempotent even under a race.
        builder.HasIndex(s => s.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");

        builder.HasIndex(s => s.CustomerId);
        builder.HasIndex(s => s.UploadedByAdminId);
        builder.HasIndex(s => s.Period);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => new { s.CustomerId, s.Period });

        // Business rule: at most one live statement per customer per period. The filter excludes
        // revoked rows (status 2 = Revoked), so a correction can be re-issued once the existing
        // statement is revoked, while history is preserved. This is the hard guard behind the
        // handler's friendly pre-check, and it holds even under a concurrent race.
        builder.HasIndex(s => new { s.CustomerId, s.Period })
            .IsUnique()
            .HasFilter("status <> 2")
            .HasDatabaseName("ix_statements_customer_id_period_active");

        builder.HasOne(s => s.Customer)
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
