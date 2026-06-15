// [INTENT]: Logica di retention/erasure GDPR (S2), estratta dal BackgroundService per essere SCOPED e testabile.
// Anonimizza i dati personali delle prenotazioni oltre la retention e purga le email outbox inviate datate
// (contengono PII nell'HTML). Operazione CROSS-tenant.

namespace WebAgency_BookingSystem.Infrastructure.Services;

/// <summary>Esegue la pulizia GDPR. Restituisce (prenotazioni anonimizzate, email outbox purgate).</summary>
internal interface IDataRetentionService
{
    Task<(int AnonymizedBookings, int PurgedOutbox)> PurgeAsync(CancellationToken ct = default);
}
