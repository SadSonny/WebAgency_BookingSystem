// [INTENT]: Mapping EF Core dell'entità Tenant (tabella tenants). Radice dell'aggregato: NON ha query
// filter. Definisce lunghezze, default e l'indice univoco sullo slug.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Slug).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(255);
        builder.Property(t => t.SiteUrl).IsRequired().HasMaxLength(500);
        builder.Property(t => t.OwnerEmail).IsRequired().HasMaxLength(255);
        builder.Property(t => t.Timezone).IsRequired().HasMaxLength(100).HasDefaultValue("Europe/Rome");
        builder.Property(t => t.NotificationMethod).IsRequired().HasMaxLength(50).HasDefaultValue("email");
        builder.Property(t => t.MinAdvanceHours).HasDefaultValue(1);
        builder.Property(t => t.MinCancellationHours).HasDefaultValue(24);
        builder.Property(t => t.VisibleDaysAhead).HasDefaultValue(30);
        builder.Property(t => t.StaffChoiceEnabled).HasDefaultValue(true);
        builder.Property(t => t.Active).HasDefaultValue(true);

        builder.HasIndex(t => t.Slug).IsUnique();
    }
}
