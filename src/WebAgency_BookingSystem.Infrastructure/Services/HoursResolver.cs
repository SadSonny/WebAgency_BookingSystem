// [INTENT]: Risoluzione condivisa della finestra oraria effettiva di un giorno, usata sia dal calcolo di
// disponibilità (per ogni giorno del range) sia dalla verifica del singolo slot in fase di prenotazione.
// Centralizza la regola: chiusura straordinaria > giorno chiuso > orari staff (se presenti) > orari tenant.

using WebAgency_BookingSystem.Core.Availability;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Infrastructure.Services;

/// <summary>
/// Determina la finestra oraria prenotabile di un giorno a partire da orari tenant/staff e chiusure.
/// </summary>
internal static class HoursResolver
{
    /// <summary>
    /// Restituisce la <see cref="DayWindow"/> effettiva del giorno, oppure null se non prenotabile
    /// (chiusura straordinaria, giorno chiuso, o staff non disponibile quel giorno).
    /// </summary>
    public static DayWindow? ResolveWindow(
        DateOnly date,
        IReadOnlyDictionary<DayOfWeekIndex, TenantBusinessHours> hoursByDay,
        IReadOnlyDictionary<DayOfWeekIndex, StaffBusinessHours> staffHoursByDay,
        IReadOnlyList<TenantSpecialClosure> closures,
        Guid? staffId)
    {
        if (closures.Any(c => date >= c.DateFrom && date <= c.DateTo))
        {
            return null;
        }

        var dow = (DayOfWeekIndex)(int)date.DayOfWeek;

        if (!hoursByDay.TryGetValue(dow, out TenantBusinessHours? tenantDay) || !tenantDay.IsOpen
            || tenantDay.OpenTime is null || tenantDay.CloseTime is null)
        {
            return null;
        }

        if (staffId is not null && staffHoursByDay.TryGetValue(dow, out StaffBusinessHours? staffDay))
        {
            if (!staffDay.IsAvailable || staffDay.StartTime is null || staffDay.EndTime is null)
            {
                return null;
            }

            return new DayWindow(staffDay.StartTime.Value, staffDay.EndTime.Value, staffDay.BreakStart, staffDay.BreakEnd);
        }

        return new DayWindow(tenantDay.OpenTime.Value, tenantDay.CloseTime.Value, tenantDay.BreakStart, tenantDay.BreakEnd);
    }
}
