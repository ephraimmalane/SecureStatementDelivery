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

        builder.Property(s => s.DocumentId)
            .HasMaxLength(128);

        // Per-customer uniqueness across non-null document ids only, so statements uploaded without a
        // DocumentId are unaffected. This is the hard guard that makes ingestion idempotent even under
        // a race; scoping by customer means source ids only need to be unique within a customer.
        builder.HasIndex(s => new { s.CustomerId, s.DocumentId })
            .IsUnique()
            .HasFilter("document_id IS NOT NULL")
            .HasDatabaseName("ix_statements_customer_id_document_id");

        // SHA-256 hex fingerprint of the plaintext bytes (64 chars).
        builder.Property(s => s.ContentHash)
            .HasMaxLength(64);

        // Per-(customer, period) uniqueness on the content fingerprint: the same file (identical bytes)
        // uploaded via any channel for the same period deduplicates regardless of DocumentId or file
        // name. Scoped to the period so two legitimately-different but byte-identical statements in
        // different periods (e.g. no-activity months) are never merged. Null is unconstrained.
        builder.HasIndex(s => new { s.CustomerId, s.Period, s.ContentHash })
            .IsUnique()
            .HasFilter("content_hash IS NOT NULL")
            .HasDatabaseName("ix_statements_customer_id_period_content_hash");

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
