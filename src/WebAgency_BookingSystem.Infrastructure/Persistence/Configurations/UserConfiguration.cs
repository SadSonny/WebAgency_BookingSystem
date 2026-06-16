// [INTENT]: Mapping EF Core di User (tabella users), admin di tenant (AD-02). role persistito come stringa.
// Email univoca globale (login per sola email): un'email = un account = un'attività. PasswordHash nullable
// fino ad attivazione account.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).IsRequired().HasMaxLength(255);
        builder.Property(u => u.PasswordHash).HasMaxLength(255); // nullable: account non ancora attivato
        builder.Property(u => u.SecurityStamp).IsRequired();
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(50);
        builder.Property(u => u.Active).HasDefaultValue(true);

        // Email univoca GLOBALE (login per sola email): un'email = un account = un'attività.
        builder.HasIndex(u => u.Email).IsUnique();

        builder.HasOne(u => u.Tenant)
            .WithMany(t => t.Users)
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
