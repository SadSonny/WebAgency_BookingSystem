// [INTENT]: Mapping EF Core di OutboxEmail (tabella outbox_email). Enum persistiti come stringa (convenzione
// di progetto). Indice (status, next_attempt_at) per la query di polling del dispatcher, che cerca le righe
// Pending eleggibili. FK al tenant in cascade. I corpi HTML/testo non hanno limite di lunghezza (text).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class OutboxEmailConfiguration : IEntityTypeConfiguration<OutboxEmail>
{
    public void Configure(EntityTypeBuilder<OutboxEmail> builder)
    {
        builder.ToTable("outbox_email");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Kind).IsRequired().HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.Status).IsRequired().HasConversion<string>().HasMaxLength(20);

        builder.Property(e => e.ToEmail).IsRequired().HasMaxLength(320);
        builder.Property(e => e.ToName).HasMaxLength(200);
        builder.Property(e => e.Subject).IsRequired().HasMaxLength(300);
        builder.Property(e => e.HtmlBody).IsRequired();
        builder.Property(e => e.TextBody).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(1000);

        // WHY: il dispatcher interroga le righe Pending con next_attempt_at <= now; questo indice copre la query.
        builder.HasIndex(e => new { e.Status, e.NextAttemptAt });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
