// [INTENT]: Mapping EF Core di Service (tabella services). buffer_position persistito come stringa,
// base_price come numeric(10,2). Indice (tenant_id, active) per la lista servizi attivi. Il soft delete
// è gestito dal global query filter (DeletedAt == null) definito nel DbContext.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(255);
        builder.Property(s => s.Category).HasMaxLength(100);
        builder.Property(s => s.BasePrice).HasPrecision(10, 2);
        builder.Property(s => s.ParallelSlots).HasDefaultValue(1);
        builder.Property(s => s.Active).HasDefaultValue(true);
        builder.Property(s => s.BufferPosition).HasConversion<string>().HasMaxLength(10);

        builder.HasIndex(s => s.TenantId);
        builder.HasIndex(s => new { s.TenantId, s.Active });

        builder.HasOne(s => s.Tenant)
            .WithMany(t => t.Services)
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
