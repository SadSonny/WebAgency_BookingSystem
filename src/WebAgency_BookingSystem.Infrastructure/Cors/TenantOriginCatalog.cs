// [INTENT]: Snapshot in-memory, thread-safe, delle origini CORS ammesse (derivate dai siteUrl dei tenant
// attivi). Esiste perché la callback CORS (SetIsOriginAllowed) è SINCRONA e gira sul path di richiesta: non
// può interrogare il DB. Il TenantOriginRefreshJob aggiorna periodicamente lo snapshot; la callback legge
// solo da qui in O(1). Singleton condiviso tra il job (scrittura) e la policy CORS (lettura).

namespace WebAgency_BookingSystem.Infrastructure.Cors;

/// <summary>
/// Catalogo delle origini ammesse per il CORS, aggiornato in background e interrogato a ogni richiesta.
/// </summary>
public sealed class TenantOriginCatalog
{
    // WHY: riferimento immutabile sostituito atomicamente (Volatile) invece di mutare l'insieme in-place.
    // Le letture (per-richiesta, frequenti) restano lock-free; la scrittura (rara) rimpiazza l'intero set.
    private volatile IReadOnlySet<string> _origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Numero di origini attualmente in catalogo (diagnostica/log).</summary>
    public int Count => _origins.Count;

    /// <summary>True se l'origine indicata appartiene a un tenant attivo (confronto case-insensitive).</summary>
    public bool IsAllowed(string? origin) =>
        !string.IsNullOrEmpty(origin) && _origins.Contains(origin);

    /// <summary>Sostituisce atomicamente l'intero insieme di origini ammesse. Chiamato dal refresh job.</summary>
    public void Replace(IEnumerable<string> origins) =>
        _origins = new HashSet<string>(origins, StringComparer.OrdinalIgnoreCase);
}
