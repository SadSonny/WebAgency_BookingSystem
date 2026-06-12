// [INTENT]: Orari settimanali del singolo staff (0=Dom..6=Sab). Se uno staff NON ha righe qui, la
// disponibilità usa gli orari del tenant. TenantId è denormalizzato per il global query filter.

using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Fascia oraria di lavoro di un membro dello staff per un singolo giorno della settimana.
/// </summary>
public class StaffBusinessHours
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Staff a cui appartiene l'orario.</summary>
    public Guid StaffId { get; set; }

    /// <summary>Tenant (denormalizzato per il query filter).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Giorno della settimana (0=Domenica .. 6=Sabato).</summary>
    public DayOfWeekIndex DayOfWeek { get; set; }

    /// <summary>Se false, lo staff non lavora quel giorno e gli orari sono null.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>Inizio turno (null se non disponibile).</summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Fine turno (null se non disponibile).</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>Inizio pausa (null se nessuna pausa).</summary>
    public TimeOnly? BreakStart { get; set; }

    /// <summary>Fine pausa (null se nessuna pausa).</summary>
    public TimeOnly? BreakEnd { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Staff? Staff { get; set; }
    public Tenant? Tenant { get; set; }
}
