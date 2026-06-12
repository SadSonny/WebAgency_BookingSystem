// [INTENT]: Traccia di audit per azioni rilevanti (creazione/disdetta prenotazione, creazione tenant).
// L'IP è memorizzato SOLO anonimizzato (ultimo ottetto rimosso) per conformità GDPR; nessun dato personale
// del cliente va nei log. Action e Actor sono stringhe a valori controllati (vedi spec schema).

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Evento di audit immutabile. Le righe non vengono mai aggiornate, solo inserite.
/// </summary>
public class AuditLog
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant a cui si riferisce l'azione.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Prenotazione collegata; null per azioni non legate a una prenotazione.</summary>
    public Guid? BookingId { get; set; }

    /// <summary>Azione, es. <c>booking_created</c>, <c>booking_cancelled_by_customer</c>, <c>tenant_created</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Attore, es. <c>customer</c> | <c>owner</c> | <c>system</c> | <c>provisioning</c>.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>IP anonimizzato (ultimo ottetto rimosso), es. <c>192.168.1.xxx</c>; null se non disponibile.</summary>
    public string? IpAnonymized { get; set; }

    /// <summary>Metadati aggiuntivi non strutturati (JSON serializzato), persistiti come JSONB.</summary>
    public string? Metadata { get; set; }

    /// <summary>Istante dell'evento (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Tenant? Tenant { get; set; }
}
