using Domain.DownloadTokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.DownloadTokens;

internal sealed class DownloadTokenConfiguration : IEntityTypeConfiguration<DownloadToken>
{
    public void Configure(EntityTypeBuilder<DownloadToken> builder)
    {
        builder.HasKey(dt => dt.Id);

        builder.Property(dt => dt.TokenHash)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(dt => dt.IpAddress)
            .HasMaxLength(45);

        builder.Property(dt => dt.IsUsed).HasDefaultValue(false);
        builder.Property(dt => dt.IsSingleUse).IsRequired();
        builder.Property(dt => dt.ExpiresAt).IsRequired();
        builder.Property(dt => dt.CreatedAt).IsRequired();

        builder.HasIndex(dt => dt.TokenHash).IsUnique();
        builder.HasIndex(dt => dt.StatementId);
        builder.HasIndex(dt => dt.UserId);
        builder.HasIndex(dt => dt.ExpiresAt);
    }
}
