// [INTENT]: Accesso ai dati del tenant e delle sue configurazioni (orari, chiusure) e risoluzione
// dell'API key. La risoluzione per hash NON è tenant-scoped (precede la conoscenza del tenant), quindi
// bypassa il global query filter nell'implementazione.

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Core.Abstractions.Repositories;

/// <summary>
/// Repository del tenant e delle sue configurazioni pubbliche.
/// </summary>
public interface ITenantRepository
{
    /// <summary>Restituisce il tenant per Id, oppure null se inesistente.</summary>
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Risolve un tenant ATTIVO a partire dall'hash SHA-256 di un'API key, considerando solo chiavi
    /// attive. Restituisce null se la chiave non esiste, è revocata, o il tenant è disattivato.
    /// </summary>
    Task<Tenant?> ResolveActiveByApiKeyHashAsync(string keyHash, CancellationToken ct = default);

    /// <summary>Restituisce gli orari settimanali del tenant (una riga per giorno presente).</summary>
    Task<IReadOnlyList<TenantBusinessHours>> GetBusinessHoursAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Restituisce le chiusure straordinarie ancora rilevanti, cioè con <c>date_to &gt;= fromInclusive</c>.
    /// </summary>
    Task<IReadOnlyList<TenantSpecialClosure>> GetActiveSpecialClosuresAsync(
        Guid tenantId, DateOnly fromInclusive, CancellationToken ct = default);
}
