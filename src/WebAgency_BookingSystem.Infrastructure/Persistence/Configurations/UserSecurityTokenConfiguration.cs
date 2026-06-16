// [INTENT]: Mapping EF Core di UserSecurityToken (tabella user_security_token). Enum come stringa. Indice su
// token_hash per la ricerca in validazione. FK all'utente in cascade.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class UserSecurityTokenConfiguration : IEntityTypeConfiguration<UserSecurityToken>
{
    public void Configure(EntityTypeBuilder<UserSecurityToken> builder)
    {
        builder.ToTable("user_security_token");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Purpose).IsRequired().HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(t => t.TokenHash);
        builder.HasIndex(t => new { t.UserId, t.Purpose });

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
