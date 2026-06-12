// [INTENT]: Orchestrazione delle prenotazioni pubbliche (IBookingService): creazione ATOMICA con advisory
// lock PostgreSQL (previene doppie prenotazioni sullo stesso slot), consultazione e disdetta via token.
// La verifica di disponibilità dentro la transazione riusa AvailabilityCalculator per coerenza con l'endpoint
// di availability. Le email post-commit usano lo stub no-op in V1 (AD-06).

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Availability;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal sealed class BookingService : IBookingService
{
    private const int LockRetryDelayMs = 200;

    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantRepository _tenants;
    private readonly IServiceRepository _services;
    private readonly IStaffRepository _staff;
    private readonly IBookingRepository _bookings;
    private readonly IEmailService _email;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        BookingSystemDbContext db,
        ITenantContext tenantContext,
        ITenantRepository tenants,
        IServiceRepository services,
        IStaffRepository staff,
        IBookingRepository bookings,
        IEmailService email,
        ILogger<BookingService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _tenants = tenants;
        _services = services;
        _staff = staff;
        _bookings = bookings;
        _email = email;
        _logger = logger;
    }

    public async Task<Result<CreateBookingResponse>> CreateAsync(
        CreateBookingRequest request, string? clientIpAnonymized, CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return Error.Validation("validation_error", "Formato data non valido. Usare yyyy-MM-dd.");
        }

        if (!TimeOnly.TryParseExact(request.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
        {
            return Error.Validation("validation_error", "Formato orario non valido. Usare HH:mm.");
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
        DateTime tenantNow = TenantTime.Now(tenant.Timezone);

        long lockKey = ComputeLockKey(tenantId, request.ServiceId, date, time);

        // WHY: usiamo pg_try_advisory_xact_lock (non bloccante) invece di un lock di riga, perché lo slot
        // potrebbe non avere ancora alcuna prenotazione (nessuna riga da lockare). La chiave hashata su
        // tenant+servizio+data+ora dà granularità minima. Il lock si rilascia automaticamente a fine transazione.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        if (!await TryAcquireSlotLockAsync(lockKey, ct))
        {
            await Task.Delay(LockRetryDelayMs, ct);
            if (!await TryAcquireSlotLockAsync(lockKey, ct))
            {
                // WHY (R-04): distinguiamo questo 409 (CONTESA: un'altra transazione tiene il lock sullo
                // stesso slot) dal 409 di capacità esaurita più sotto. Il client vede lo stesso codice, ma
                // i log permettono di capire se è concorrenza o slot realmente pieno.
                _logger.LogWarning(
                    "Prenotazione in conflitto: advisory lock non acquisito (slot conteso) per service {ServiceId} il {Date} alle {Time}",
                    request.ServiceId, date, time);
                return Error.Conflict("slot_unavailable",
                    "Lo slot selezionato non è più disponibile. Ricarica la disponibilità e riprova.");
            }
        }

        Result<CreateBookingResponse> rulesCheck = await CheckBookingRulesAsync(tenant, service, request.StaffId, date, time, tenantNow, ct);
        if (rulesCheck.IsFailure)
        {
            return rulesCheck;
        }

        decimal? price = await ResolvePriceAsync(service, request.StaffId, ct);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceId = request.ServiceId,
            StaffId = request.StaffId,
            BookingDate = date,
            BookingTime = time,
            DurationMinutes = service.DurationMinutes,
            CustomerName = request.Customer.Name,
            CustomerPhone = request.Customer.Phone,
            CustomerEmail = request.Customer.Email,
            CustomerNotes = request.Customer.Notes,
            GdprConsent = request.GdprConsent,
            GdprConsentAt = nowUtc,
            Status = BookingStatus.Confirmed,
            CancellationToken = Guid.NewGuid(),
            PriceAtBooking = price,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        await _bookings.AddAsync(booking, ct);
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BookingId = booking.Id,
            Action = "booking_created",
            Actor = "customer",
            IpAnonymized = clientIpAnonymized,
            CreatedAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Prenotazione creata {BookingId} per service {ServiceId} staff {StaffId} il {Date} alle {Time}",
            booking.Id, request.ServiceId, request.StaffId, date, time);

        // Post-commit: in V1 l'email è no-op (istantanea). In V2 (Brevo) valutare invio fire-and-forget.
        await _email.SendBookingConfirmationAsync(booking, ct);
        if (string.Equals(tenant.NotificationMethod, "email", StringComparison.OrdinalIgnoreCase))
        {
            await _email.SendOwnerNotificationAsync(booking, ct);
        }

        return Result.Success(new CreateBookingResponse(booking.Id, booking.Status.ToApiString(), booking.CancellationToken));
    }

    public async Task<Result<BookingDetailResponse>> GetByTokenAsync(Guid bookingId, Guid token, CancellationToken ct = default)
    {
        Booking? booking = await _bookings.GetByIdAndTokenAsync(bookingId, token, ct);
        if (booking is null)
        {
            return NeutralNotFound<BookingDetailResponse>();
        }

        Tenant tenant = (await _tenants.GetByIdAsync(_tenantContext.TenantId!.Value, ct))!;
        DateTime tenantNow = TenantTime.Now(tenant.Timezone);
        DateTime bookingMoment = booking.BookingDate.ToDateTime(booking.BookingTime);
        DateTime deadline = bookingMoment.AddHours(-tenant.MinCancellationHours);

        bool canCancel = booking.Status == BookingStatus.Confirmed && tenantNow < deadline;

        var response = new BookingDetailResponse(
            booking.Id,
            booking.Status.ToApiString(),
            booking.BookingDate.ToString("yyyy-MM-dd"),
            booking.BookingTime.ToString("HH:mm"),
            booking.DurationMinutes,
            new BookingServiceRef(booking.ServiceId, booking.Service?.Name ?? string.Empty),
            booking.StaffId is Guid sid ? new BookingStaffRef(sid, booking.Staff?.Name ?? string.Empty) : null,
            new BookingCustomerRef(booking.CustomerName, booking.CustomerEmail),
            canCancel,
            deadline.ToString("yyyy-MM-ddTHH:mm:ss"));

        return Result.Success(response);
    }

    public async Task<Result<CancelBookingResponse>> CancelAsync(Guid bookingId, Guid token, CancellationToken ct = default)
    {
        Booking? booking = await _bookings.GetByIdAndTokenAsync(bookingId, token, ct);
        if (booking is null)
        {
            return NeutralNotFound<CancelBookingResponse>();
        }

        if (booking.Status != BookingStatus.Confirmed)
        {
            return Error.Validation("booking_not_cancellable", "La prenotazione non è in uno stato disdicibile.");
        }

        Tenant tenant = (await _tenants.GetByIdAsync(_tenantContext.TenantId!.Value, ct))!;
        DateTime tenantNow = TenantTime.Now(tenant.Timezone);
        DateTime deadline = booking.BookingDate.ToDateTime(booking.BookingTime).AddHours(-tenant.MinCancellationHours);

        if (tenantNow >= deadline)
        {
            return Error.Forbidden("cancellation_deadline_exceeded",
                $"Non è possibile disdire con meno di {tenant.MinCancellationHours} ore di preavviso.");
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = nowUtc;
        booking.CancellationReason = "customer";
        booking.UpdatedAt = nowUtc;

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = booking.TenantId,
            BookingId = booking.Id,
            Action = "booking_cancelled_by_customer",
            Actor = "customer",
            CreatedAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Prenotazione {BookingId} disdetta dal cliente", booking.Id);

        await _email.SendCancellationConfirmationAsync(booking, ct);

        return Result.Success(new CancelBookingResponse(booking.Id, booking.Status.ToApiString(), "Prenotazione disdetta con successo."));
    }

    // Ri-verifica regole di business + disponibilità DENTRO la transazione (con dati freschi sotto lock).
    private async Task<Result<CreateBookingResponse>> CheckBookingRulesAsync(
        Tenant tenant, Service service, Guid? staffId, DateOnly date, TimeOnly time, DateTime tenantNow, CancellationToken ct)
    {
        Guid tenantId = tenant.Id;

        IReadOnlyList<TenantBusinessHours> businessHours = await _tenants.GetBusinessHoursAsync(tenantId, ct);
        IReadOnlyList<TenantSpecialClosure> closures = await _tenants.GetActiveSpecialClosuresAsync(tenantId, date, ct);

        Dictionary<DayOfWeekIndex, StaffBusinessHours> staffHoursByDay = new();
        if (staffId is Guid sid)
        {
            IReadOnlyList<StaffBusinessHours> staffHours = await _staff.GetBusinessHoursAsync(sid, ct);
            staffHoursByDay = staffHours.ToDictionary(h => h.DayOfWeek);
        }

        var hoursByDay = businessHours.ToDictionary(h => h.DayOfWeek);
        DayWindow? window = HoursResolver.ResolveWindow(date, hoursByDay, staffHoursByDay, closures, staffId);
        if (window is null)
        {
            return Error.Validation("validation_error", "Il giorno o l'orario selezionato non è prenotabile.");
        }

        DateTime bookingMoment = date.ToDateTime(time);
        if (bookingMoment < tenantNow.AddHours(tenant.MinAdvanceHours))
        {
            return Error.Validation("validation_error", "Lo slot selezionato non rispetta l'anticipo minimo di prenotazione.");
        }

        if (date > DateOnly.FromDateTime(tenantNow).AddDays(tenant.VisibleDaysAhead))
        {
            return Error.Validation("validation_error", "La data selezionata è oltre il periodo prenotabile.");
        }

        IReadOnlyList<Booking> dayBookings = staffId is Guid staffFilter
            ? await _bookings.GetConfirmedByStaffInRangeAsync(staffFilter, date, date, ct)
            : await _bookings.GetConfirmedByServiceInRangeAsync(service.Id, date, date, ct);

        var slots = dayBookings.Select(b => new BookingSlot(b.BookingTime, b.DurationMinutes, b.ServiceId, b.StaffId)).ToList();
        var config = new ServiceSlotConfig(
            service.DurationMinutes, service.ParallelSlots,
            service.BufferEnabled ? service.BufferMinutes : 0, service.BufferPosition);

        if (!AvailabilityCalculator.IsSlotAvailable(time, window, config, service.Id, staffId, slots))
        {
            // WHY (R-04): qui lo slot è realmente PIENO/non valido alla ri-verifica sotto lock (capacità
            // esaurita o regola oraria), distinto dalla contesa di lock loggata in CreateAsync.
            _logger.LogInformation(
                "Prenotazione rifiutata: slot non disponibile alla ri-verifica per service {ServiceId} il {Date} alle {Time}",
                service.Id, date, time);
            return Error.Conflict("slot_unavailable",
                "Lo slot selezionato non è più disponibile. Ricarica la disponibilità e riprova.");
        }

        return Result.Success(default(CreateBookingResponse)!);
    }

    private async Task<decimal?> ResolvePriceAsync(Service service, Guid? staffId, CancellationToken ct)
    {
        if (staffId is Guid sid)
        {
            StaffService? link = await _db.StaffServices
                .FirstOrDefaultAsync(ss => ss.StaffId == sid && ss.ServiceId == service.Id, ct);
            if (link?.PriceOverride is decimal overridePrice)
            {
                return overridePrice;
            }
        }

        return service.BasePrice;
    }

    private async Task<bool> TryAcquireSlotLockAsync(long lockKey, CancellationToken ct)
    {
        // EF mappa lo scalare booleano su una colonna chiamata "Value".
        List<bool> rows = await _db.Database
            .SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock({0}) AS \"Value\"", lockKey)
            .ToListAsync(ct);
        return rows.Count > 0 && rows[0];
    }

    private static long ComputeLockKey(Guid tenantId, Guid serviceId, DateOnly date, TimeOnly time)
    {
        string input = $"{tenantId}:{serviceId}:{date:yyyy-MM-dd}:{time:HH:mm}";
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToInt64(bytes, 0);
    }

    // Risposta neutra: non rivela se l'id esiste con token errato (sicurezza, spec 03).
    private static Result<T> NeutralNotFound<T>() =>
        Error.NotFound("not_found", "Prenotazione non trovata o token non valido.");
}
