// [INTENT]: Mapping EF Core di TenantApiKey (tabella tenant_api_keys). Conserva solo l'hash SHA-256
// (univoco) della chiave. Indice univoco su key_hash per la risoluzione veloce X-Api-Key -> tenant.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class TenantApiKeyConfiguration : IEntityTypeConfiguration<TenantApiKey>
{
    public void Configure(EntityTypeBuilder<TenantApiKey> builder)
    {
        builder.HasKey(k => k.Id);

        builder.Property(k => k.KeyHash).IsRequired().HasMaxLength(255);
        builder.Property(k => k.KeyPrefix).IsRequired().HasMaxLength(8);
        builder.Property(k => k.Description).HasMaxLength(255);
        builder.Property(k => k.Active).HasDefaultValue(true);

        builder.HasIndex(k => k.KeyHash).IsUnique();
        builder.HasIndex(k => k.TenantId);

        builder.HasOne(k => k.Tenant)
            .WithMany(t => t.ApiKeys)
            .HasForeignKey(k => k.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
