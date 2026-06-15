// [INTENT]: Voce di un appuntamento multi-servizio (T1.3). Un appuntamento (Booking) è svolto da UN solo
// operatore in servizi CONSECUTIVI: ogni BookingItem è un servizio della sequenza con durata e prezzo
// "congelati" alla creazione. Sequence definisce l'ordine. La durata totale dell'appuntamento (Booking.
// DurationMinutes) è la somma delle durate degli item; il prezzo (Booking.PriceAtBooking) la somma dei prezzi.

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Singolo servizio all'interno di un appuntamento. Anche le prenotazioni a servizio singolo hanno un item.
/// </summary>
public class BookingItem
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Appuntamento di appartenenza.</summary>
    public Guid BookingId { get; set; }

    /// <summary>Tenant proprietario (denormalizzato per scoping/coerenza).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Servizio erogato in questa voce.</summary>
    public Guid ServiceId { get; set; }

    /// <summary>Ordine nella sequenza (0 = primo). I servizi sono svolti in quest'ordine, consecutivi.</summary>
    public int Sequence { get; set; }

    /// <summary>Durata in minuti, snapshot del servizio alla creazione.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Prezzo snapshot del servizio alla creazione; null se non prezzato.</summary>
    public decimal? PriceAtBooking { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Booking? Booking { get; set; }
    public Service? Service { get; set; }
}
