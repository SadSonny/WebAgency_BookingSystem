// [INTENT]: Prenotazione di un cliente finale. Data e ora sono LOCALI del tenant (non UTC), come da
// contratto API. Durata e prezzo sono snapshot al momento della creazione (immuni a modifiche future del
// servizio). Lo StaffId è opzionale (AD-04). Il CancellationToken permette al cliente di consultare/disdire
// senza autenticazione.

using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Appuntamento prenotato. Identificabile pubblicamente solo combinando Id + <see cref="CancellationToken"/>.
/// </summary>
public class Booking : IAuditableEntity
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant proprietario.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Servizio prenotato.</summary>
    public Guid ServiceId { get; set; }

    /// <summary>Staff scelto; null se nessuno staff specifico (AD-04).</summary>
    public Guid? StaffId { get; set; }

    /// <summary>Data dell'appuntamento (ora locale del tenant).</summary>
    public DateOnly BookingDate { get; set; }

    /// <summary>Ora di inizio dell'appuntamento (ora locale del tenant).</summary>
    public TimeOnly BookingTime { get; set; }

    /// <summary>Durata in minuti, snapshot del servizio al momento della prenotazione.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Nome del cliente finale.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Telefono del cliente finale.</summary>
    public string CustomerPhone { get; set; } = string.Empty;

    /// <summary>Email del cliente finale.</summary>
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>Note opzionali del cliente.</summary>
    public string? CustomerNotes { get; set; }

    /// <summary>Consenso GDPR fornito.</summary>
    public bool GdprConsent { get; set; } = true;

    /// <summary>Istante del consenso GDPR (UTC).</summary>
    public DateTimeOffset GdprConsentAt { get; set; }

    /// <summary>Stato corrente della prenotazione.</summary>
    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;

    /// <summary>Token opaco per consultazione/disdetta pubblica senza login.</summary>
    public Guid CancellationToken { get; set; }

    /// <summary>Istante della disdetta (UTC); null se non disdetta.</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Origine della disdetta: <c>customer</c> | <c>owner</c> | <c>system</c>.</summary>
    public string? CancellationReason { get; set; }

    /// <summary>Istante in cui è stato segnato il no-show (UTC).</summary>
    public DateTimeOffset? NoShowMarkedAt { get; set; }

    /// <summary>Prezzo snapshot al momento della prenotazione.</summary>
    public decimal? PriceAtBooking { get; set; }

    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Istante dell'ultimo aggiornamento (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Tenant? Tenant { get; set; }
    public Service? Service { get; set; }
    public Staff? Staff { get; set; }

    /// <summary>Servizi che compongono l'appuntamento, in ordine (T1.3). Almeno uno; il primo combacia con
    /// <see cref="ServiceId"/>. Per i servizi multipli la durata/prezzo del Booking sono la somma degli item.</summary>
    public ICollection<BookingItem> Items { get; set; } = [];
}
