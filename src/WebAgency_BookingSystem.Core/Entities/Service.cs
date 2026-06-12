// [INTENT]: Servizio prenotabile offerto da un tenant (es. "Taglio uomo"). Porta la durata, il prezzo
// base, il numero di postazioni parallele e la configurazione del buffer PER SERVIZIO (AD-03).
// Soft delete tramite DeletedAt: un servizio eliminato resta in DB per integrità storica delle prenotazioni.

using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Prestazione prenotabile. La durata e il prezzo vengono "congelati" sulla prenotazione al momento
/// della creazione, così modifiche successive non alterano le prenotazioni esistenti.
/// </summary>
public class Service : IAuditableEntity
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant proprietario del servizio.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Nome del servizio.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Categoria opzionale, es. "Capelli", "Barba".</summary>
    public string? Category { get; set; }

    /// <summary>Descrizione estesa opzionale.</summary>
    public string? Description { get; set; }

    /// <summary>Durata dell'appuntamento in minuti (&gt; 0).</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Prezzo base; può essere sovrascritto per singolo staff (<see cref="StaffService.PriceOverride"/>).</summary>
    public decimal? BasePrice { get; set; }

    /// <summary>Postazioni parallele: quante prenotazioni simultanee accetta lo stesso slot (&gt;= 1).</summary>
    public int ParallelSlots { get; set; } = 1;

    /// <summary>Se true, il buffer è attivo per questo servizio (AD-03).</summary>
    public bool BufferEnabled { get; set; }

    /// <summary>Minuti di buffer da applicare attorno all'appuntamento.</summary>
    public int BufferMinutes { get; set; }

    /// <summary>Dove applicare il buffer rispetto all'appuntamento.</summary>
    public BufferPosition BufferPosition { get; set; } = BufferPosition.After;

    /// <summary>Se false, il servizio non è prenotabile né mostrato.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Ordine di visualizzazione nel frontend.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Istante dell'ultimo aggiornamento (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Istante di soft delete (UTC); null se non eliminato.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Tenant? Tenant { get; set; }
    public ICollection<StaffService> StaffServices { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
}
