// [INTENT]: Radice dell'aggregato multi-tenant. Rappresenta un'attività commerciale con le sue regole
// di prenotazione e configurazione. NON è tenant-scoped (è la tabella di risoluzione del tenant): su di
// essa NON si applica il global query filter. Tutte le altre entità referenziano il suo Id come TenantId.

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Attività commerciale registrata sulla piattaforma. Aggrega servizi, staff, orari e prenotazioni.
/// </summary>
public class Tenant : IAuditableEntity
{
    /// <summary>Identificativo univoco del tenant (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Slug univoco usato negli URL e nel provisioning, es. <c>mario-barbershop</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Nome visualizzato dell'attività.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL del sito dell'attività.</summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>Email del titolare, destinataria delle notifiche.</summary>
    public string OwnerEmail { get; set; } = string.Empty;

    /// <summary>Timezone IANA del tenant (default <c>Europe/Rome</c>). Usata per convertire gli orari in response.</summary>
    public string Timezone { get; set; } = "Europe/Rome";

    /// <summary>Anticipo minimo richiesto per prenotare, in ore.</summary>
    public int MinAdvanceHours { get; set; } = 1;

    /// <summary>Preavviso minimo per disdire, in ore.</summary>
    public int MinCancellationHours { get; set; } = 24;

    /// <summary>Quanti giorni in avanti sono prenotabili dal cliente.</summary>
    public int VisibleDaysAhead { get; set; } = 30;

    /// <summary>Se true, il cliente può scegliere lo staff in fase di prenotazione.</summary>
    public bool StaffChoiceEnabled { get; set; } = true;

    /// <summary>Metodo di notifica: <c>email</c> oppure <c>none</c>.</summary>
    public string NotificationMethod { get; set; } = "email";

    /// <summary>Se false, il tenant è disattivato e le sue API key non risolvono.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Istante dell'ultimo aggiornamento (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public ICollection<TenantApiKey> ApiKeys { get; set; } = [];
    public ICollection<TenantBusinessHours> BusinessHours { get; set; } = [];
    public ICollection<TenantSpecialClosure> SpecialClosures { get; set; } = [];
    public ICollection<Service> Services { get; set; } = [];
    public ICollection<Staff> Staff { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];
    public ICollection<User> Users { get; set; } = [];
}
