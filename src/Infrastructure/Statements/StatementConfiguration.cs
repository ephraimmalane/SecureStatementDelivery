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

        builder.Property(s => s.Status)
            .IsRequired();

        builder.Property(s => s.RevokedReason)
            .HasMaxLength(500);

        builder.HasIndex(s => s.CustomerId);
        builder.HasIndex(s => s.UploadedByAdminId);
        builder.HasIndex(s => s.Period);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => new { s.CustomerId, s.Period });

        builder.HasOne(s => s.Customer)
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
