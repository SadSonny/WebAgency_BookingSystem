// [INTENT]: Mapping EF Core di Booking (tabella bookings). status persistito come stringa; prezzo
// numeric(10,2). Indici per disponibilità (tenant+service+date+status), per data, per token di disdetta
// e per staff+data. FK a service/staff in Restrict (le prenotazioni non vengono cancellate a cascata).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.CustomerName).IsRequired().HasMaxLength(255);
        builder.Property(b => b.CustomerPhone).IsRequired().HasMaxLength(50);
        builder.Property(b => b.CustomerEmail).IsRequired().HasMaxLength(255);
        builder.Property(b => b.CancellationReason).HasMaxLength(255);
        builder.Property(b => b.PriceAtBooking).HasPrecision(10, 2);
        builder.Property(b => b.GdprConsent).HasDefaultValue(true);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(50);

        builder.HasIndex(b => new { b.TenantId, b.BookingDate });
        builder.HasIndex(b => b.CancellationToken);
        builder.HasIndex(b => new { b.TenantId, b.ServiceId, b.BookingDate, b.Status });
        builder.HasIndex(b => new { b.StaffId, b.BookingDate });
        // P3: supporta lo scan cross-tenant del job promemoria (Confermate non ancora promemoria-te in finestra).
        builder.HasIndex(b => new { b.Status, b.ReminderSentAt, b.BookingDate });

        builder.HasOne(b => b.Tenant)
            .WithMany(t => t.Bookings)
            .HasForeignKey(b => b.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Service)
            .WithMany(s => s.Bookings)
            .HasForeignKey(b => b.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Staff)
            .WithMany(s => s.Bookings)
            .HasForeignKey(b => b.StaffId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
