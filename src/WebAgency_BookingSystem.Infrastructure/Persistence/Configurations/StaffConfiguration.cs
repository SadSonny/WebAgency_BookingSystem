// [INTENT]: Mapping EF Core di Staff (tabella staff). Indice (tenant_id, active) per la lista staff attivi.
// Soft delete gestito dal global query filter (DeletedAt == null) nel DbContext.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(255);
        builder.Property(s => s.Role).HasMaxLength(100);
        builder.Property(s => s.Specialization).HasMaxLength(255);
        builder.Property(s => s.PhotoUrl).HasMaxLength(500);
        builder.Property(s => s.Active).HasDefaultValue(true);

        builder.HasIndex(s => s.TenantId);
        builder.HasIndex(s => new { s.TenantId, s.Active });

        builder.HasOne(s => s.Tenant)
            .WithMany(t => t.Staff)
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
