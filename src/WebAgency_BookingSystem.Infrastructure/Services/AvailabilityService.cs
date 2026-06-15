// [INTENT]: Orchestrazione della disponibilità (IAvailabilityService). Valida l'input, carica i dati dai
// repository, risolve la finestra oraria per giorno (orari staff o tenant + chiusure) e delega il calcolo
// puro ad AvailabilityCalculator. Converte il risultato nei DTO pubblici (orari come stringhe locali).

using Microsoft.Extensions.Logging;
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
    private readonly ILogger<AvailabilityService> _logger;

    public AvailabilityService(
        ITenantContext tenantContext,
        ITenantRepository tenants,
        IServiceRepository services,
        IStaffRepository staff,
        IBookingRepository bookings,
        ILogger<AvailabilityService> logger)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _services = services;
        _staff = staff;
        _bookings = bookings;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<AvailabilityDayResponse>>> GetAvailabilityAsync(
        AvailabilityRequest request, CancellationToken ct = default)
    {
        _logger.LogDebug("Disponibilità richiesta per service {ServiceId} staff {StaffId} dal {From} al {To}",
            request.ServiceId, request.StaffId, request.DateFrom, request.DateTo);

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

        // Tenant già caricato dal middleware: nessuna query ridondante (R-21).
        Tenant tenant = _tenantContext.Tenant!;
        Guid tenantId = tenant.Id;

        // Caricamento dati una sola volta per l'intero range.
        IReadOnlyList<TenantBusinessHours> businessHours = await _tenants.GetBusinessHoursAsync(tenantId, ct);
        Dictionary<DayOfWeekIndex, TenantBusinessHours> hoursByDay = businessHours.ToDictionary(h => h.DayOfWeek);

        IReadOnlyList<TenantSpecialClosure> closures = await _tenants.GetActiveSpecialClosuresAsync(tenantId, request.DateFrom, ct);

        DateTime tenantNow = TenantTime.Now(tenant.Timezone);
        ServiceSlotConfig config = ServiceSlotConfig.From(service);

        // T1.2: chi consideriamo. Staff specifico → solo quello. "Qualsiasi" → tutti gli operatori attivi che
        // ESEGUONO il servizio; se nessuno lo esegue (servizio non legato a una persona) si ricade sul modello
        // a parallelSlots.
        List<Guid> staffIds;
        if (request.StaffId is Guid only)
        {
            staffIds = [only];
        }
        else
        {
            IReadOnlyList<Staff> qualified = await _staff.GetActiveByServiceAsync(request.ServiceId, ct);
            staffIds = qualified.Select(s => s.Id).ToList();
        }

        var days = new List<AvailabilityDayResponse>();

        if (staffIds.Count == 0)
        {
            // Servizio senza operatori: capacità aggregata a parallelSlots, slot senza operatore.
            IReadOnlyList<Booking> existing = await _bookings.GetConfirmedByServiceInRangeAsync(
                request.ServiceId, request.DateFrom, request.DateTo, ct);
            ILookup<DateOnly, BookingSlot> bookingsByDate = existing
                .ToLookup(b => b.BookingDate, b => new BookingSlot(b.BookingTime, b.DurationMinutes, b.ServiceId, b.StaffId));

            for (DateOnly date = request.DateFrom; date <= request.DateTo; date = date.AddDays(1))
            {
                DayWindow? window = HoursResolver.ResolveWindow(
                    date, hoursByDay, new Dictionary<DayOfWeekIndex, StaffBusinessHours>(), closures, null);
                if (window is null)
                {
                    continue;
                }

                IReadOnlyList<SlotResult> slots = AvailabilityCalculator.ComputeDay(
                    date, window, config, request.ServiceId, null, bookingsByDate[date].ToList(), tenantNow, tenant.MinAdvanceHours);
                if (slots.Count == 0)
                {
                    continue;
                }

                days.Add(new AvailabilityDayResponse(date.ToString("yyyy-MM-dd"),
                    slots.Select(s => new SlotResponse(s.Time.ToString("HH:mm"), null, s.Available)).ToList()));
            }

            return Result.Success<IReadOnlyList<AvailabilityDayResponse>>(days);
        }

        // P1: carichiamo orari/assenze/prenotazioni di TUTTI gli operatori in 3 query e raggruppiamo in memoria,
        // invece di 3 query per operatore. La disponibilità "qualsiasi" è poi calcolata per ciascuno senza altre query.
        ILookup<Guid, StaffBusinessHours> hoursByStaff =
            (await _staff.GetBusinessHoursForStaffAsync(staffIds, ct)).ToLookup(h => h.StaffId);
        ILookup<Guid, StaffTimeOff> timeOffByStaff =
            (await _staff.GetTimeOffForStaffInRangeAsync(staffIds, request.DateFrom, request.DateTo, ct)).ToLookup(t => t.StaffId);
        ILookup<Guid, Booking> bookingsByStaff =
            (await _bookings.GetConfirmedByStaffIdsInRangeAsync(staffIds, request.DateFrom, request.DateTo, ct)).ToLookup(b => b.StaffId!.Value);

        var merged = new SortedDictionary<DateOnly, SortedDictionary<TimeOnly, bool>>();
        foreach (Guid qualifiedStaffId in staffIds)
        {
            AccumulateStaff(qualifiedStaffId, request, hoursByDay, closures, config, tenant, tenantNow,
                hoursByStaff[qualifiedStaffId].ToDictionary(h => h.DayOfWeek),
                timeOffByStaff[qualifiedStaffId].ToList(),
                bookingsByStaff[qualifiedStaffId].ToLookup(b => b.BookingDate,
                    b => new BookingSlot(b.BookingTime, b.DurationMinutes, b.ServiceId, b.StaffId)),
                merged);
        }

        foreach ((DateOnly date, SortedDictionary<TimeOnly, bool> slotMap) in merged)
        {
            if (slotMap.Count == 0)
            {
                continue;
            }

            // Lo staffId nella response riflette la richiesta: l'id se specifico, null se "qualsiasi" (l'operatore
            // viene auto-assegnato solo alla creazione della prenotazione).
            var slotDtos = slotMap.Select(kv => new SlotResponse(kv.Key.ToString("HH:mm"), request.StaffId, kv.Value)).ToList();
            days.Add(new AvailabilityDayResponse(date.ToString("yyyy-MM-dd"), slotDtos));
        }

        return Result.Success<IReadOnlyList<AvailabilityDayResponse>>(days);
    }

    // Calcola la disponibilità di UN operatore nel range (orari/assenze/prenotazioni GIÀ caricati) e la fonde
    // in OR nella mappa per data→orario→disponibile. Riusa l'algoritmo puro per-staff. Nessuna query (P1).
    private static void AccumulateStaff(
        Guid staffId, AvailabilityRequest request,
        Dictionary<DayOfWeekIndex, TenantBusinessHours> hoursByDay,
        IReadOnlyList<TenantSpecialClosure> closures,
        ServiceSlotConfig config, Tenant tenant, DateTime tenantNow,
        Dictionary<DayOfWeekIndex, StaffBusinessHours> staffHoursByDay,
        IReadOnlyList<StaffTimeOff> timeOff,
        ILookup<DateOnly, BookingSlot> bookingsByDate,
        SortedDictionary<DateOnly, SortedDictionary<TimeOnly, bool>> merged)
    {
        for (DateOnly date = request.DateFrom; date <= request.DateTo; date = date.AddDays(1))
        {
            DayWindow? window = HoursResolver.ResolveWindow(date, hoursByDay, staffHoursByDay, closures, staffId);
            if (window is null)
            {
                continue;
            }

            DateOnly d = date;
            if (timeOff.Any(t => t.IsFullDay && d >= t.DateFrom && d <= t.DateTo))
            {
                continue; // assenza a giornata intera
            }

            IReadOnlyList<TimeInterval> blocks = timeOff
                .Where(t => !t.IsFullDay && d >= t.DateFrom && d <= t.DateTo)
                .Select(t => new TimeInterval(t.StartTime!.Value, t.EndTime!.Value))
                .ToList();

            IReadOnlyList<SlotResult> slots = AvailabilityCalculator.ComputeDay(
                date, window, config, request.ServiceId, staffId, bookingsByDate[date].ToList(), tenantNow, tenant.MinAdvanceHours, blocks);
            if (slots.Count == 0)
            {
                continue;
            }

            if (!merged.TryGetValue(date, out SortedDictionary<TimeOnly, bool>? slotMap))
            {
                slotMap = new SortedDictionary<TimeOnly, bool>();
                merged[date] = slotMap;
            }

            foreach (SlotResult s in slots)
            {
                slotMap[s.Time] = slotMap.TryGetValue(s.Time, out bool existing) ? existing || s.Available : s.Available;
            }
        }
    }
}
