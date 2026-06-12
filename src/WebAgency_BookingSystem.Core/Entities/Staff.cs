// [INTENT]: Membro dello staff di un tenant (es. un barbiere). Opzionale nelle prenotazioni: se il cliente
// non sceglie uno staff, la prenotazione ha StaffId null e la disponibilità si basa sui parallelSlots del
// servizio (AD-04). Soft delete tramite DeletedAt per preservare lo storico delle prenotazioni.

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Operatore che eroga i servizi. Può avere orari propri e un sottoinsieme di servizi abilitati.
/// </summary>
public class Staff : IAuditableEntity
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant proprietario.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Nome del membro dello staff.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Ruolo opzionale, es. "Barbiere Senior".</summary>
    public string? Role { get; set; }

    /// <summary>Specializzazione opzionale.</summary>
    public string? Specialization { get; set; }

    /// <summary>URL foto opzionale.</summary>
    public string? PhotoUrl { get; set; }

    /// <summary>Se false, lo staff non è selezionabile né mostrato.</summary>
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
    public ICollection<StaffBusinessHours> BusinessHours { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
}
