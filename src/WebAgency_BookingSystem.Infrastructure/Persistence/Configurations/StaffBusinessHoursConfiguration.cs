// [INTENT]: Mapping EF Core di StaffBusinessHours (tabella staff_business_hours). day_of_week SMALLINT.
// Vincolo univoco (staff_id, day_of_week). Tenant in Restrict per evitare percorsi di cascata multipli.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class StaffBusinessHoursConfiguration : IEntityTypeConfiguration<StaffBusinessHours>
{
    public void Configure(EntityTypeBuilder<StaffBusinessHours> builder)
    {
        builder.HasKey(h => h.Id);

        builder.Property(h => h.DayOfWeek).HasConversion<short>();
        builder.Property(h => h.IsAvailable).HasDefaultValue(true);

        builder.HasIndex(h => new { h.StaffId, h.DayOfWeek }).IsUnique();
        builder.HasIndex(h => h.TenantId);

        builder.HasOne(h => h.Staff)
            .WithMany(s => s.BusinessHours)
            .HasForeignKey(h => h.StaffId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.Tenant)
            .WithMany()
            .HasForeignKey(h => h.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
