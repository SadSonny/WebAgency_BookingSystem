// [INTENT]: Genera i token JWT admin. L'implementazione (Infrastructure) firma il token con il segreto
// configurato e vi inserisce i claim user_id, tenant_id e role (AD-08). Astratto qui per disaccoppiare
// il servizio di autenticazione dalla libreria JWT concreta.

using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Produce token JWT firmati per l'autenticazione admin.
/// </summary>
public interface IJwtTokenGenerator
{
    /// <summary>
    /// Genera un JWT con i claim dell'utente admin e restituisce il token e il suo istante di scadenza (UTC).
    /// </summary>
    (string Token, DateTimeOffset ExpiresAt) Generate(Guid userId, Guid tenantId, UserRole role, Guid securityStamp);
}
