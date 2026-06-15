// [INTENT]: Accesso agli utenti admin. La ricerca per (tenant, email) avviene PRIMA della risoluzione del
// tenant corrente (al login), quindi l'implementazione bypassa il global query filter e filtra esplicitamente
// per tenant.

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Core.Abstractions.Repositories;

/// <summary>
/// Repository degli utenti admin di tenant.
/// </summary>
public interface IUserRepository
{
    /// <summary>Restituisce l'utente per (tenant, email), oppure null se inesistente.</summary>
    Task<User?> GetByTenantAndEmailAsync(Guid tenantId, string email, CancellationToken ct = default);

    /// <summary>
    /// Registra un tentativo di login fallito (S3): incrementa il contatore e, raggiunta la soglia, imposta
    /// il blocco per la durata indicata (azzerando il contatore). Persiste subito.
    /// </summary>
    Task RegisterFailedAttemptAsync(Guid userId, int lockoutThreshold, TimeSpan lockoutDuration, CancellationToken ct = default);

    /// <summary>Registra un login riuscito (S3): azzera contatore e blocco, aggiorna LastLoginAt. Persiste subito.</summary>
    Task RegisterSuccessfulLoginAsync(Guid userId, CancellationToken ct = default);
}
