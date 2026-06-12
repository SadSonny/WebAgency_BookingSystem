// [INTENT]: Associazione M:M tra Staff e Service (quali servizi eroga un operatore), con eventuale
// override di prezzo. TenantId è denormalizzato per consentire il global query filter su questa entità.

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Abilitazione di un membro dello staff a erogare un servizio, con prezzo opzionale dedicato.
/// </summary>
public class StaffService
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Staff abilitato.</summary>
    public Guid StaffId { get; set; }

    /// <summary>Servizio erogato.</summary>
    public Guid ServiceId { get; set; }

    /// <summary>Tenant (denormalizzato per il query filter).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Prezzo specifico per questo staff; null = usa <see cref="Service.BasePrice"/>.</summary>
    public decimal? PriceOverride { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Staff? Staff { get; set; }
    public Service? Service { get; set; }
    public Tenant? Tenant { get; set; }
}
