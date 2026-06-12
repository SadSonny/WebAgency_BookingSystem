// [INTENT]: Mapping EF Core di TenantBusinessHours (tabella tenant_business_hours). day_of_week è
// persistito come SMALLINT. Vincolo univoco (tenant_id, day_of_week): una sola riga per giorno.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class TenantBusinessHoursConfiguration : IEntityTypeConfiguration<TenantBusinessHours>
{
    public void Configure(EntityTypeBuilder<TenantBusinessHours> builder)
    {
        builder.HasKey(h => h.Id);

        builder.Property(h => h.DayOfWeek).HasConversion<short>();
        builder.Property(h => h.IsOpen).HasDefaultValue(true);

        builder.HasIndex(h => new { h.TenantId, h.DayOfWeek }).IsUnique();

        builder.HasOne(h => h.Tenant)
            .WithMany(t => t.BusinessHours)
            .HasForeignKey(h => h.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
