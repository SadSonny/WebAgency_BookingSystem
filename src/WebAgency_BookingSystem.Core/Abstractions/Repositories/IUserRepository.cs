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
}
