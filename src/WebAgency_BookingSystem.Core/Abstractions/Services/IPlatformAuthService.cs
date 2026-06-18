// [INTENT]: Autenticazione agency-admin di piattaforma. Verifica email+password sull'identità globale PlatformAdmin
// (nessun tenant) e rilascia un JWT di piattaforma. Gli esiti negativi sono veicolati via Result con un errore
// NEUTRO (401) per non rivelare quale parte delle credenziali è errata.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Servizio di autenticazione per gli amministratori di piattaforma (agency-admin).
/// </summary>
public interface IPlatformAuthService
{
    /// <summary>
    /// Autentica il PlatformAdmin (email + password) e restituisce un token JWT di piattaforma. In caso di
    /// credenziali non valide o account disattivato/non attivato restituisce un Unauthorized neutro.
    /// </summary>
    Task<Result<AdminTokenResponse>> LoginAsync(PlatformLoginRequest request, CancellationToken ct = default);
}
