// [INTENT]: Orchestrazione della disponibilità (IAvailabilityService). Valida l'input, carica i dati dai
// repository, risolve la finestra oraria per giorno (orari staff o tenant + chiusure) e delega il calcolo
// puro ad AvailabilityCalculator. Converte il risultato nei DTO pubblici (orari come stringhe locali).

using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Availability;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal sealed class AvailabilityService : IAvailabilityService
{
    private const int MaxRangeDays = 31;

    private readonly ITenantContext _tenantContext;
    private readonly ITenantRepository _tenants;
    private readonly IServiceRepository _services;
    private readonly IStaffRepository _staff;
    private readonly IBookingRepository _bookings;

    public AvailabilityService(
        ITenantContext tenantContext,
        ITenantRepository tenants,
        IServiceRepository services,
        IStaffRepository staff,
        IBookingRepository bookings)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _services = services;
        _staff = staff;
        _bookings = bookings;
    }

    public async Task<Result<IReadOnlyList<AvailabilityDayResponse>>> GetAvailabilityAsync(
        AvailabilityRequest request, CancellationToken ct = default)
    {
        if (request.DateFrom > request.DateTo)
        {
            return Error.Validation("validation_error", "L'intervallo di date non è valido: dateFrom è successivo a dateTo.");
        }

        if (request.DateTo.DayNumber - request.DateFrom.DayNumber > MaxRangeDays)
        {
            return Error.Validation("validation_error", $"L'intervallo di date non può superare {MaxRangeDays} giorni.");
        }

        Service? service = await _services.GetActiveByIdAsync(request.ServiceId, ct);
        if (service is null)
        {
            return Error.NotFound("not_found", "Servizio non trovato o non attivo.");
        }

        if (request.StaffId is Guid staffId)
        {
            Staff? staff = await _staff.GetActiveByIdAsync(staffId, ct);
            if (staff is null)
            {
                return Error.Validation("validation_error", "Staff non trovato o non attivo.");
            }

            if (!await _staff.ExecutesServiceAsync(staffId, request.ServiceId, ct))
            {
                return Error.Validation("validation_error", "Lo staff selezionato non esegue il servizio richiesto.");
            }
        }

        Guid tenantId = _tenantContext.TenantId!.Value;
        Tenant tenant = (await _tenants.GetByIdAsync(tenantId, ct))!;

        // Caricamento dati una sola volta per l'intero range.
        IReadOnlyList<TenantBusinessHours> businessHours = await _tenants.GetBusinessHoursAsync(tenantId, ct);
        Dictionary<DayOfWeekIndex, TenantBusinessHours> hoursByDay = businessHours.ToDictionary(h => h.DayOfWeek);

        IReadOnlyList<TenantSpecialClosure> closures = await _tenants.GetActiveSpecialClosuresAsync(tenantId, request.DateFrom, ct);

        Dictionary<DayOfWeekIndex, StaffBusinessHours> staffHoursByDay = new();
        if (request.StaffId is Guid sid)
        {
            IReadOnlyList<StaffBusinessHours> staffHours = await _staff.GetBusinessHoursAsync(sid, ct);
            staffHoursByDay = staffHours.ToDictionary(h => h.DayOfWeek);
        }

        IReadOnlyList<Booking> existing = request.StaffId is Guid staffFilter
            ? await _bookings.GetConfirmedByStaffInRangeAsync(staffFilter, request.DateFrom, request.DateTo, ct)
            : await _bookings.GetConfirmedByServiceInRangeAsync(request.ServiceId, request.DateFrom, request.DateTo, ct);

        ILookup<DateOnly, BookingSlot> bookingsByDate = existing
            .ToLookup(b => b.BookingDate, b => new BookingSlot(b.BookingTime, b.DurationMinutes, b.ServiceId, b.StaffId));

        DateTime tenantNow = TenantTime.Now(tenant.Timezone);
        var config = new ServiceSlotConfig(
            service.DurationMinutes,
            service.ParallelSlots,
            service.BufferEnabled ? service.BufferMinutes : 0,
            service.BufferPosition);

        var days = new List<AvailabilityDayResponse>();
        for (DateOnly date = request.DateFrom; date <= request.DateTo; date = date.AddDays(1))
        {
            DayWindow? window = HoursResolver.ResolveWindow(date, hoursByDay, staffHoursByDay, closures, request.StaffId);
            if (window is null)
            {
                continue; // giorno chiuso / chiusura straordinaria / staff non disponibile
            }

            IReadOnlyList<BookingSlot> dayBookings = bookingsByDate[date].ToList();
            IReadOnlyList<SlotResult> slots = AvailabilityCalculator.ComputeDay(
                date, window, config, request.ServiceId, request.StaffId, dayBookings, tenantNow, tenant.MinAdvanceHours);

            if (slots.Count == 0)
            {
                continue; // nessuno slot candidato (passato, finestra troppo stretta): giorno non incluso
            }

            var slotDtos = slots
                .Select(s => new SlotResponse(s.Time.ToString("HH:mm"), request.StaffId, s.Available))
                .ToList();
            days.Add(new AvailabilityDayResponse(date.ToString("yyyy-MM-dd"), slotDtos));
        }

        return Result.Success<IReadOnlyList<AvailabilityDayResponse>>(days);
    }
}
