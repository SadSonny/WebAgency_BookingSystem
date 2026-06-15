// [INTENT]: Mapping EF Core di StaffTimeOff (tabella staff_time_off, T1.1). Indice (staff_id, date_from,
// date_to) per le query di disponibilità per operatore in un range. FK a staff e tenant in cascade.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class StaffTimeOffConfiguration : IEntityTypeConfiguration<StaffTimeOff>
{
    public void Configure(EntityTypeBuilder<StaffTimeOff> builder)
    {
        builder.ToTable("staff_time_off");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Reason).HasMaxLength(300);

        // WHY: la disponibilità interroga le assenze di un operatore che intersecano il range richiesto.
        builder.HasIndex(t => new { t.StaffId, t.DateFrom, t.DateTo });

        builder.Ignore(t => t.IsFullDay);

        builder.HasOne(t => t.Staff)
            .WithMany()
            .HasForeignKey(t => t.StaffId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Tenant)
            .WithMany()
            .HasForeignKey(t => t.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
