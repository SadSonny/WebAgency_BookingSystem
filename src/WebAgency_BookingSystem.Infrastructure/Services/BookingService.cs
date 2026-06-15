// [INTENT]: Orchestrazione delle prenotazioni pubbliche (IBookingService): creazione ATOMICA con advisory
// lock PostgreSQL (previene doppie prenotazioni sullo stesso slot), consultazione e disdetta via token.
// La verifica di disponibilità dentro la transazione riusa AvailabilityCalculator per coerenza con l'endpoint
// di availability. Le email sono ACCODATE nella outbox dentro la stessa transazione (PH-3): vengono inviate
// dal dispatcher in background con retry, garantendo consegna senza compromettere l'atomicità del booking.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Availability;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Email;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Services;

internal sealed class BookingService : IBookingService
{
    // WHY (PH-2): timeout massimo di attesa del lock di slot. Il lock è BLOCCANTE (le prenotazioni legittime
    // concorrenti aspettano il loro turno breve invece di ricevere un 409 spurio), ma non deve attendere
    // all'infinito sotto contesa patologica: oltre questa soglia consideriamo lo slot conteso → 409.
    private const int LockTimeoutMs = 5000;
    private const string LockTimeoutSqlState = "55P03"; // lock_not_available

    private readonly BookingSystemDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantRepository _tenants;
    private readonly IServiceRepository _services;
    private readonly IStaffRepository _staff;
    private readonly IBookingRepository _bookings;
    private readonly IEmailOutbox _outbox;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        BookingSystemDbContext db,
        ITenantContext tenantContext,
        ITenantRepository tenants,
        IServiceRepository services,
        IStaffRepository staff,
        IBookingRepository bookings,
        IEmailOutbox outbox,
        ILogger<BookingService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _tenants = tenants;
        _services = services;
        _staff = staff;
        _bookings = bookings;
        _outbox = outbox;
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

        // Tenant già caricato dal middleware (R-21).
        Tenant tenant = _tenantContext.Tenant!;
        Guid tenantId = tenant.Id;
        DateTime tenantNow = TenantTime.Now(tenant.Timezone);

        long lockKey = ComputeLockKey(tenantId, request.ServiceId, date, time);

        // WHY (R-12): con EnableRetryOnFailure le transazioni manuali devono girare dentro un'execution
        // strategy, che può rieseguire l'intero blocco su errori transitori del DB. La creazione vera e propria
        // (advisory lock + ri-verifica + insert) sta qui dentro; l'invio email resta FUORI (post-commit), così
        // un eventuale retry non può inviare email doppie.
        Booking? createdBooking = null;
        var strategy = _db.Database.CreateExecutionStrategy();

        Result<CreateBookingResponse> outcome = await strategy.ExecuteAsync(async () =>
        {
            createdBooking = null;

            // WHY (PH-2): advisory lock BLOCCANTE invece di try+singolo-retry. La chiave hashata su
            // tenant+servizio+data+ora serializza le sole prenotazioni sullo STESSO slot (non di righe, lo slot
            // potrebbe non avere ancora prenotazioni) e si rilascia a fine transazione. Il lock di SLOT (non
            // per-posto) è ciò che rende corretta la ri-verifica di capacità sotto `parallelSlots > 1`: due
            // richieste legittime concorrenti vengono accodate (niente 409 spurio), e ognuna riconta i posti
            // occupati con dati freschi. Un lock per-posto sarebbe invece NON sicuro (più transazioni
            // leggerebbero lo stesso conteggio e supererebbero la capacità). Il lock_timeout impedisce attese
            // illimitate sotto contesa patologica.
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                await AcquireSlotLockAsync(lockKey, ct);
            }
            catch (PostgresException ex) when (ex.SqlState == LockTimeoutSqlState)
            {
                // R-04: CONTESA reale (lock non acquisito entro il timeout), distinta dalla capacità esaurita.
                _logger.LogWarning(
                    "Prenotazione in conflitto: advisory lock non acquisito entro {TimeoutMs}ms (slot conteso) per service {ServiceId} il {Date} alle {Time}",
                    LockTimeoutMs, request.ServiceId, date, time);
                return Result.Failure<CreateBookingResponse>(Error.Conflict("slot_unavailable",
                    "Lo slot selezionato non è più disponibile. Ricarica la disponibilità e riprova."));
            }

            Result rulesCheck = await CheckBookingRulesAsync(tenant, service, request.StaffId, date, time, tenantNow, ct);
            if (rulesCheck.IsFailure)
            {
                return Result.Failure<CreateBookingResponse>(rulesCheck.Error);
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
                // CreatedAt/UpdatedAt valorizzati dal TimestampInterceptor (R-27).
            };

            // WHY (PH-3): accodiamo le email nella OUTBOX dentro la STESSA transazione del booking. Se il commit
            // riesce, le email sono garantite in coda (inviate dal dispatcher con retry); se fallisce (o l'execution
            // strategy riprova), anche le righe outbox sono annullate → niente email per prenotazioni inesistenti
            // né doppioni. Le navigation servono SOLO al renderer: le valorizziamo, renderizziamo, poi le azzeriamo
            // PRIMA di tracciare il booking, perché tenant/service vengono dalla cache AsNoTracking (detached) e EF
            // tenterebbe di persistirli (errore di concorrenza/insert). La riga outbox porta solo gli scalari.
            booking.Tenant = tenant;
            booking.Service = service;
            _outbox.EnqueueBookingConfirmation(booking);
            if (string.Equals(tenant.NotificationMethod, "email", StringComparison.OrdinalIgnoreCase))
            {
                _outbox.EnqueueOwnerNotification(booking);
            }
            booking.Tenant = null;
            booking.Service = null;

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

            try
            {
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // R-18: conflitto di concorrenza/vincolo in race — difesa in profondità se l'advisory lock
                // non bastasse. Mappiamo a 409 invece di lasciar propagare un 500.
                _logger.LogWarning(ex,
                    "Conflitto di persistenza nella creazione prenotazione per service {ServiceId} il {Date} alle {Time}",
                    request.ServiceId, date, time);
                return Result.Failure<CreateBookingResponse>(Error.Conflict("slot_unavailable",
                    "Lo slot selezionato non è più disponibile. Ricarica la disponibilità e riprova."));
            }

            createdBooking = booking;
            return Result.Success(new CreateBookingResponse(booking.Id, booking.Status.ToApiString(), booking.CancellationToken));
        });

        if (outcome.IsFailure || createdBooking is null)
        {
            return outcome;
        }

        _logger.LogInformation(
            "Prenotazione creata {BookingId} per service {ServiceId} staff {StaffId} il {Date} alle {Time}",
            createdBooking.Id, request.ServiceId, request.StaffId, date, time);

        // Le email sono già state accodate nella outbox dentro la transazione (PH-3): l'invio effettivo è a
        // carico del dispatcher in background, quindi qui non c'è nulla da fare post-commit.
        return outcome;
    }

    public async Task<Result<BookingDetailResponse>> GetByTokenAsync(Guid bookingId, Guid token, CancellationToken ct = default)
    {
        Booking? booking = await _bookings.GetByIdAndTokenAsync(bookingId, token, ct);
        if (booking is null)
        {
            return NeutralNotFound<BookingDetailResponse>();
        }

        Tenant tenant = _tenantContext.Tenant!;
        // Orario locale del termine ultimo (per il display), e l'ISTANTE assoluto corrispondente (per la
        // decisione canCancel, DST-corretta — PH-5).
        DateTime deadline = booking.BookingDate.ToDateTime(booking.BookingTime).AddHours(-tenant.MinCancellationHours);
        DateTimeOffset deadlineInstant = TenantTime
            .ToInstant(booking.BookingDate, booking.BookingTime, tenant.Timezone)
            .AddHours(-tenant.MinCancellationHours);

        bool canCancel = booking.Status == BookingStatus.Confirmed && DateTimeOffset.UtcNow < deadlineInstant;

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

        Tenant tenant = _tenantContext.Tenant!;
        // PH-5: confronto su istanti assoluti (DST-corretto) per decidere se il preavviso è rispettato.
        DateTimeOffset deadlineInstant = TenantTime
            .ToInstant(booking.BookingDate, booking.BookingTime, tenant.Timezone)
            .AddHours(-tenant.MinCancellationHours);

        if (DateTimeOffset.UtcNow >= deadlineInstant)
        {
            return Error.Forbidden("cancellation_deadline_exceeded",
                $"Non è possibile disdire con meno di {tenant.MinCancellationHours} ore di preavviso.");
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = nowUtc;
        booking.CancellationReason = "customer";
        // UpdatedAt valorizzato dal TimestampInterceptor (R-27).

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = booking.TenantId,
            BookingId = booking.Id,
            Action = "booking_cancelled_by_customer",
            Actor = "customer",
            CreatedAt = nowUtc,
        });

        // WHY (PH-3): accodiamo la conferma di disdetta nella outbox PRIMA del SaveChanges, così disdetta ed
        // email sono committate atomicamente. Service/Staff sono già caricati (tracked) dal repository; il Tenant
        // viene dalla cache (detached): lo usiamo per il renderer e poi lo azzeriamo, altrimenti EF tenterebbe di
        // persistere un'entità detached durante l'update della disdetta. Il FK TenantId resta valorizzato.
        booking.Tenant = tenant;
        _outbox.EnqueueCancellationConfirmation(booking);
        booking.Tenant = null;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Prenotazione {BookingId} disdetta dal cliente", booking.Id);

        return Result.Success(new CancelBookingResponse(booking.Id, booking.Status.ToApiString(), "Prenotazione disdetta con successo."));
    }

    // Ri-verifica regole di business + disponibilità DENTRO la transazione (con dati freschi sotto lock).
    // Restituisce un Result senza valore: veicola solo l'esito (successo o errore), non un payload (R-20).
    private async Task<Result> CheckBookingRulesAsync(
        Tenant tenant, Service service, Guid? staffId, DateOnly date, TimeOnly time, DateTime tenantNow, CancellationToken ct)
    {
        Guid tenantId = tenant.Id;

        IReadOnlyList<TenantBusinessHours> businessHours = await _tenants.GetBusinessHoursAsync(tenantId, ct);
        IReadOnlyList<TenantSpecialClosure> closures = await _tenants.GetActiveSpecialClosuresAsync(tenantId, date, ct);

        Dictionary<DayOfWeekIndex, StaffBusinessHours> staffHoursByDay = new();
        IReadOnlyList<StaffTimeOff> staffTimeOff = [];
        if (staffId is Guid sid)
        {
            IReadOnlyList<StaffBusinessHours> staffHours = await _staff.GetBusinessHoursAsync(sid, ct);
            staffHoursByDay = staffHours.ToDictionary(h => h.DayOfWeek);
            staffTimeOff = await _staff.GetTimeOffInRangeAsync(sid, date, date, ct);
        }

        var hoursByDay = businessHours.ToDictionary(h => h.DayOfWeek);
        DayWindow? window = HoursResolver.ResolveWindow(date, hoursByDay, staffHoursByDay, closures, staffId);
        if (window is null)
        {
            return Error.Validation("validation_error", "Il giorno o l'orario selezionato non è prenotabile.");
        }

        // T1.1: assenza dell'operatore — giornata intera blocca del tutto; le fasce parziali sono passate al
        // calcolatore come intervalli "duri" che invalidano gli slot sovrapposti.
        if (staffTimeOff.Any(t => t.IsFullDay))
        {
            return Error.Validation("validation_error", "L'operatore selezionato non è disponibile nella data scelta.");
        }

        IReadOnlyList<TimeInterval> staffBlocks = staffTimeOff
            .Where(t => !t.IsFullDay)
            .Select(t => new TimeInterval(t.StartTime!.Value, t.EndTime!.Value))
            .ToList();

        // PH-5: confronto su istanti assoluti (DST-corretto) per l'anticipo minimo: lo slot locale è convertito
        // nel suo istante e confrontato con "ora UTC + anticipo", invece di sommare ore in ora locale "naive".
        DateTimeOffset bookingInstant = TenantTime.ToInstant(date, time, tenant.Timezone);
        if (bookingInstant < DateTimeOffset.UtcNow.AddHours(tenant.MinAdvanceHours))
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
        ServiceSlotConfig config = ServiceSlotConfig.From(service);

        if (!AvailabilityCalculator.IsSlotAvailable(time, window, config, service.Id, staffId, slots, staffBlocks))
        {
            // WHY (R-04): qui lo slot è realmente PIENO/non valido alla ri-verifica sotto lock (capacità
            // esaurita o regola oraria), distinto dalla contesa di lock loggata in CreateAsync.
            _logger.LogInformation(
                "Prenotazione rifiutata: slot non disponibile alla ri-verifica per service {ServiceId} il {Date} alle {Time}",
                service.Id, date, time);
            return Error.Conflict("slot_unavailable",
                "Lo slot selezionato non è più disponibile. Ricarica la disponibilità e riprova.");
        }

        return Result.Success();
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

    // Acquisisce il lock di slot in modo BLOCCANTE, ma con un lock_timeout locale alla transazione: se non lo
    // ottiene entro LockTimeoutMs, PostgreSQL solleva un errore 55P03 (lock_not_available) che il chiamante
    // mappa a 409 (slot conteso). pg_advisory_xact_lock rilascia automaticamente a fine transazione.
    private async Task AcquireSlotLockAsync(long lockKey, CancellationToken ct)
    {
        // WHY: SET LOCAL vale solo dentro la transazione corrente (già aperta). Il valore è una costante
        // interna (non input utente), quindi l'interpolazione è sicura.
        await _db.Database.ExecuteSqlRawAsync($"SET LOCAL lock_timeout = '{LockTimeoutMs}ms'", ct);
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [lockKey], ct);
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
