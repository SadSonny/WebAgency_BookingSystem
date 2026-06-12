// [INTENT]: Operazioni admin su orari settimanali (6.13) e chiusure straordinarie (6.14) del tenant corrente.
// Entrambe sostituiscono in blocco l'intero set. Validano formati/vincoli e restituiscono errori via Result.
// Le mutazioni degli orari invalidano la cache pubblica del tenant (R-22).

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>
/// Gestione amministrativa di orari e chiusure del tenant.
/// </summary>
public interface IAdminScheduleManager
{
    /// <summary>Sostituisce gli orari settimanali del tenant. Valida i formati e gli intervalli.</summary>
    Task<Result> SetBusinessHoursAsync(SetBusinessHoursRequest request, CancellationToken ct = default);

    /// <summary>Sostituisce le chiusure straordinarie del tenant. Valida date e intervalli.</summary>
    Task<Result> SetClosuresAsync(SetClosuresRequest request, CancellationToken ct = default);
}
