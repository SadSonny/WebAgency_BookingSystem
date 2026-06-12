// [INTENT]: Orari di apertura settimanali del tenant, una riga per giorno (0=Dom..6=Sab). Con eventuale
// pausa pranzo (break). Sono la base per l'algoritmo di disponibilità quando non c'è uno staff specifico
// o lo staff non ha orari propri.

using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Fascia oraria di apertura del tenant per un singolo giorno della settimana.
/// </summary>
public class TenantBusinessHours
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant a cui appartiene l'orario.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Giorno della settimana (0=Domenica .. 6=Sabato).</summary>
    public DayOfWeekIndex DayOfWeek { get; set; }

    /// <summary>Se false, il tenant è chiuso quel giorno e gli orari sono null.</summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>Ora di apertura (null se chiuso).</summary>
    public TimeOnly? OpenTime { get; set; }

    /// <summary>Ora di chiusura (null se chiuso).</summary>
    public TimeOnly? CloseTime { get; set; }

    /// <summary>Inizio pausa (null se nessuna pausa).</summary>
    public TimeOnly? BreakStart { get; set; }

    /// <summary>Fine pausa (null se nessuna pausa).</summary>
    public TimeOnly? BreakEnd { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Tenant? Tenant { get; set; }
}
