// [INTENT]: Mapping EF Core di AuditLog (tabella audit_log, nome singolare come da schema). metadata in
// JSONB. Indice (tenant_id, created_at DESC) per le query di audit recenti. booking_id è una colonna
// nullable senza FK (può riferirsi a prenotazioni anche dopo eventuali rimozioni logiche).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Actor).IsRequired().HasMaxLength(100);
        builder.Property(a => a.IpAnonymized).HasMaxLength(50);
        builder.Property(a => a.Metadata).HasColumnType("jsonb");

        builder.HasIndex(a => new { a.TenantId, a.CreatedAt }).IsDescending(false, true);

        builder.HasOne(a => a.Tenant)
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
