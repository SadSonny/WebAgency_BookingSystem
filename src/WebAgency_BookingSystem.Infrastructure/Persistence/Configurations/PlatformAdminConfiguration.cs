// [INTENT]: Mapping EF Core di PlatformAdmin (tabella platform_admin). Email univoca globale. Nessun global query
// filter tenant (identità di piattaforma, cross-tenant).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class PlatformAdminConfiguration : IEntityTypeConfiguration<PlatformAdmin>
{
    public void Configure(EntityTypeBuilder<PlatformAdmin> builder)
    {
        builder.ToTable("platform_admin");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Email).IsRequired().HasMaxLength(255);
        builder.Property(p => p.PasswordHash).HasMaxLength(255);
        builder.Property(p => p.SecurityStamp).IsRequired();
        builder.Property(p => p.Active).HasDefaultValue(true);
        builder.HasIndex(p => p.Email).IsUnique();
    }
}
