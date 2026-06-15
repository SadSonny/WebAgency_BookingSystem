// [INTENT]: Logica di dispatch della outbox email (PH-3), estratta dal BackgroundService per essere SCOPED e
// testabile in isolamento (come IExpiredBookingCleaner). Processa le email Pending eleggibili: le invia via
// IEmailSender e ne aggiorna lo stato con retry/backoff. Operazione CROSS-tenant (usa IgnoreQueryFilters).

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>Invia le email Pending della outbox eleggibili, aggiornandone lo stato (Sent/retry/Failed).</summary>
internal interface IOutboxEmailProcessor
{
    /// <summary>Processa un batch di email pendenti eleggibili. Restituisce il numero di righe elaborate.</summary>
    Task<int> ProcessPendingAsync(CancellationToken ct = default);
}
