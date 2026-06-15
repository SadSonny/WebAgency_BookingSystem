// [INTENT]: Mapping EF Core di BookingItem (tabella booking_items, T1.3). FK al booking in cascade (gli item
// seguono il ciclo di vita dell'appuntamento). Indice su booking_id per caricare gli item dell'appuntamento.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Persistence.Configurations;

internal sealed class BookingItemConfiguration : IEntityTypeConfiguration<BookingItem>
{
    public void Configure(EntityTypeBuilder<BookingItem> builder)
    {
        builder.ToTable("booking_items");

        builder.HasKey(i => i.Id);

        builder.HasIndex(i => i.BookingId);

        builder.HasOne(i => i.Booking)
            .WithMany(b => b.Items)
            .HasForeignKey(i => i.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        // WHY: nessuna FK navigabile a Service per non complicare i query filter; il riferimento è per id.
        builder.HasOne(i => i.Service)
            .WithMany()
            .HasForeignKey(i => i.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
