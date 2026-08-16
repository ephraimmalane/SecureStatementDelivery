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

        // The processor only ever queries for unprocessed messages ordered by age, so index the
        // claim path. A filtered index keeps it small once processed rows accumulate.
        builder.HasIndex(m => m.OccurredOnUtc)
            .HasFilter("processed_on_utc IS NULL");
    }
}
