using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Outbox;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).IsRequired();

        builder.Property(m => m.Content)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(m => m.OccurredOnUtc).IsRequired();

        builder.Property(m => m.RetryCount).HasDefaultValue(0);

        builder.HasIndex(m => m.OccurredOnUtc)
            .HasFilter("processed_on_utc IS NULL");
    }
}
