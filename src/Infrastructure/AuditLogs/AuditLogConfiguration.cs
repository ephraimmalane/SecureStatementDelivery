using Domain.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.AuditLogs;

internal sealed class DownloadAuditLogConfiguration : IEntityTypeConfiguration<DownloadAuditLog>
{
    public void Configure(EntityTypeBuilder<DownloadAuditLog> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Action).IsRequired();

        builder.Property(l => l.IpAddress)
            .HasMaxLength(45);

        builder.Property(l => l.UserAgent)
            .HasMaxLength(512);

        builder.Property(l => l.OccurredAt).IsRequired();

        builder.HasIndex(l => l.StatementId);
        builder.HasIndex(l => l.UserId);
        builder.HasIndex(l => l.OccurredAt);
        builder.HasIndex(l => l.Action);
    }
}
