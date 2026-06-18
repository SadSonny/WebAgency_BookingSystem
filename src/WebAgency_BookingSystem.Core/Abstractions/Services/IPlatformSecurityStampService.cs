// [INTENT]: Verifica che la SecurityStamp di un JWT platform sia quella corrente del PlatformAdmin (invalidazione
// JWT al cambio password). Cache-first; Invalidate svuota dopo una mutazione.

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Validazione della SecurityStamp dei JWT di piattaforma (agency-admin). Gemello di
/// <see cref="IUserSecurityStampService"/> ma sullo store separato dei PlatformAdmin.
/// </summary>
public interface IPlatformSecurityStampService
{
    /// <summary>True se <paramref name="stamp"/> coincide con la SecurityStamp corrente del PlatformAdmin.</summary>
    Task<bool> IsCurrentAsync(Guid platformAdminId, Guid stamp, CancellationToken ct = default);

    /// <summary>Invalida la voce cachata così i JWT emessi prima di una mutazione smettono di valere.</summary>
    void Invalidate(Guid platformAdminId);
}
