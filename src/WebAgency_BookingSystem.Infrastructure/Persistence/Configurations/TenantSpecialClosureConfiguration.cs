// [INTENT]: Mapping EF Core di TenantSpecialClosure (tabella tenant_special_closures). Indice composito
// su (tenant_id, date_from, date_to) per i controlli di chiusura nell'algoritmo di disponibilità.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class TenantSpecialClosureConfiguration : IEntityTypeConfiguration<TenantSpecialClosure>
{
    public void Configure(EntityTypeBuilder<TenantSpecialClosure> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Reason).HasMaxLength(255);

        builder.HasIndex(c => new { c.TenantId, c.DateFrom, c.DateTo });

        builder.HasOne(c => c.Tenant)
            .WithMany(t => t.SpecialClosures)
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
