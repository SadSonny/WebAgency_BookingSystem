// [INTENT]: Mapping EF Core di StaffService (tabella staff_services), associazione M:M staff<->servizio.
// Vincolo univoco (staff_id, service_id). price_override numeric(10,2). DeleteBehavior.Cascade dallo staff
// e al servizio; Restrict sul tenant per evitare cicli di cascata multipli su PostgreSQL.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class StaffServiceConfiguration : IEntityTypeConfiguration<StaffService>
{
    public void Configure(EntityTypeBuilder<StaffService> builder)
    {
        builder.HasKey(ss => ss.Id);

        builder.Property(ss => ss.PriceOverride).HasPrecision(10, 2);

        builder.HasIndex(ss => new { ss.StaffId, ss.ServiceId }).IsUnique();
        builder.HasIndex(ss => ss.ServiceId);
        builder.HasIndex(ss => ss.TenantId);

        builder.HasOne(ss => ss.Staff)
            .WithMany(s => s.StaffServices)
            .HasForeignKey(ss => ss.StaffId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ss => ss.Service)
            .WithMany(s => s.StaffServices)
            .HasForeignKey(ss => ss.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // WHY: Restrict sul tenant perché staff e service già cascano dal tenant; una terza cascata
        // sullo stesso percorso provocherebbe "multiple cascade paths" rifiutato da PostgreSQL.
        builder.HasOne(ss => ss.Tenant)
            .WithMany()
            .HasForeignKey(ss => ss.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
