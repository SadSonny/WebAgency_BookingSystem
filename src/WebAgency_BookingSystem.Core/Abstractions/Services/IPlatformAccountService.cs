// [INTENT]: Contratto del servizio per le operazioni sull'account agency-admin: setup/break-glass
// (crea-o-reimposta la password per email, gated da env PLATFORM_SETUP_TOKEN) e cambio password
// autenticato (verifica password corrente, aggiorna hash, invalida JWT precedenti via SecurityStamp).

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Operazioni sull'account agency-admin: setup/break-glass e cambio password autenticato.
/// </summary>
public interface IPlatformAccountService
{
    /// <summary>
    /// Crea o reimposta la password dell'agency-admin per email (break-glass).
    /// Restituisce <c>true</c> se l'admin è stato creato, <c>false</c> se la password è stata reimpostata.
    /// Fallisce con <see cref="ErrorType.NotFound"/> se PLATFORM_SETUP_TOKEN non è configurato (endpoint
    /// disabilitato), o con <see cref="ErrorType.Unauthorized"/> se il token non corrisponde.
    /// </summary>
    Task<Result<bool>> SetupAsync(PlatformSetupRequest request, CancellationToken ct = default);

    /// <summary>
    /// Cambia la password dell'agency-admin autenticato: verifica la password corrente, aggiorna l'hash
    /// e rigenera la SecurityStamp per invalidare i JWT precedenti.
    /// </summary>
    Task<Result> ChangePasswordAsync(Guid platformAdminId, ChangePasswordRequest request, CancellationToken ct = default);
}
