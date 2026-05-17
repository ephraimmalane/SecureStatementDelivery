using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Users;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(r => r.Name).IsUnique();

        builder.HasData(Role.Admin, Role.Customer);
    }
}
