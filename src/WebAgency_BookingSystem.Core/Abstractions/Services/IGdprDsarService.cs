// [INTENT]: Contratto del servizio DSAR (GDPR 4.3) tenant-scoped: esporta e cancella on-demand i dati di un
// cliente identificato per email. L'isolamento per tenant è garantito dal global query filter del DbContext.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>Operazioni DSAR (diritto d'accesso e all'oblio) sui dati di un cliente del tenant corrente.</summary>
public interface IGdprDsarService
{
    /// <summary>Esporta tutte le prenotazioni del cliente con l'email indicata (case-insensitive). Riesce sempre;
    /// la lista è vuota se non ci sono dati. Registra un evento di audit dell'accesso.</summary>
    Task<Result<CustomerDataExport>> ExportAsync(string email, CancellationToken ct = default);

    /// <summary>Anonimizza le prenotazioni e elimina le email outbox del cliente con l'email indicata. Ritorna
    /// NotFound se non c'è nulla da cancellare. Registra un evento di audit della cancellazione.</summary>
    Task<Result<ErasureResult>> EraseAsync(string email, CancellationToken ct = default);
}
