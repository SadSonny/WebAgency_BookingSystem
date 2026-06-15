// [INTENT]: Assenza/indisponibilità di un singolo operatore (T1.1): ferie, malattia, permessi. Copre un
// intervallo di giorni [DateFrom..DateTo] inclusivi; se StartTime/EndTime sono null vale per l'INTERA giornata,
// altrimenti solo per quella FASCIA oraria (in ciascun giorno del range). Esclude l'operatore dalla
// disponibilità per quei periodi, indipendentemente dai suoi orari settimanali e dalle prenotazioni.

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Periodo di indisponibilità di un membro dello staff. Tenant-scoped. Le ore sono locali del tenant.
/// </summary>
public class StaffTimeOff : IAuditableEntity
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant proprietario.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Operatore assente.</summary>
    public Guid StaffId { get; set; }

    /// <summary>Primo giorno di assenza (incluso).</summary>
    public DateOnly DateFrom { get; set; }

    /// <summary>Ultimo giorno di assenza (incluso).</summary>
    public DateOnly DateTo { get; set; }

    /// <summary>Inizio della fascia oraria di assenza; null = giornata intera.</summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Fine della fascia oraria di assenza; null = giornata intera.</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>Motivo opzionale (ferie, malattia, permesso…).</summary>
    public string? Reason { get; set; }

    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Istante dell'ultimo aggiornamento (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>True se l'assenza copre l'intera giornata (nessuna fascia oraria specificata).</summary>
    public bool IsFullDay => StartTime is null || EndTime is null;

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Tenant? Tenant { get; set; }
    public Staff? Staff { get; set; }
}
