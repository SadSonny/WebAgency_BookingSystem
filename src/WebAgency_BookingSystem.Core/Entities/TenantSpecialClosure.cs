// [INTENT]: Chiusura straordinaria del tenant su un intervallo di date (ferie, festività). Sovrascrive
// gli orari settimanali: in questi giorni non viene generato alcuno slot disponibile.

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Periodo di chiusura straordinaria. <see cref="DateFrom"/> = <see cref="DateTo"/> per un singolo giorno.
/// </summary>
public class TenantSpecialClosure
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant a cui appartiene la chiusura.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Primo giorno di chiusura (incluso).</summary>
    public DateOnly DateFrom { get; set; }

    /// <summary>Ultimo giorno di chiusura (incluso).</summary>
    public DateOnly DateTo { get; set; }

    /// <summary>Motivo della chiusura, es. "Ferie agosto".</summary>
    public string? Reason { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Tenant? Tenant { get; set; }
}
