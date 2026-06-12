// [INTENT]: Autenticazione admin (step 6.1). Verifica le credenziali e rilascia un JWT. Gli esiti negativi
// sono veicolati via Result con un errore NEUTRO (stesso messaggio per slug/email/password errati) per non
// rivelare quale parte delle credenziali è sbagliata.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Servizio di autenticazione per gli utenti admin di tenant.
/// </summary>
public interface IAdminAuthService
{
    /// <summary>
    /// Autentica l'utente (tenant slug + email + password) e restituisce un token JWT. In caso di credenziali
    /// non valide o tenant/utente disattivato restituisce un <see cref="ErrorType.Unauthorized"/> neutro.
    /// </summary>
    Task<Result<AdminTokenResponse>> LoginAsync(AdminLoginRequest request, CancellationToken ct = default);
}
