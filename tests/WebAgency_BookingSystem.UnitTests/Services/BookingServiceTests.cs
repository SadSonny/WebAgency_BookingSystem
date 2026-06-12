// [INTENT]: Unit test di BookingService (step 9.2, parte testabile senza DB reale). Coprono la logica di
// consultazione (GetByTokenAsync) e disdetta (CancelAsync): 404 neutro su id/token errati, stato non
// disdicibile (422), superamento preavviso (403), calcolo canCancel, e percorso di disdetta con audit+email.
//
// CreateAsync NON è coperto qui: usa advisory lock PostgreSQL, transazione e SQL raw, quindi richiede un
// vero Postgres → va testato in integration con Docker/Testcontainers (vedi DOCKER_SESSION_TODO.md).
// Dipendenze mockate con NSubstitute; il DbContext è EF InMemory (usato solo da CancelAsync per l'audit).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Services;

namespace WebAgency_BookingSystem.UnitTests.Services;

public class BookingServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ServiceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid BookingId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Token = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid StaffId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    // Date lontane dal "now" reale: rendono i test deterministici nonostante TenantTime usi l'orologio reale.
    private static readonly DateOnly FutureDate = new(2035, 1, 1);
    private static readonly DateOnly PastDate = new(2020, 1, 1);
    private static readonly TimeOnly At10 = new(10, 0);

    private sealed record Harness(
        BookingService Sut,
        BookingSystemDbContext Db,
        IBookingRepository Bookings,
        IEmailService Email);

    private static Harness CreateSut()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantId);

        DbContextOptions<BookingSystemDbContext> options = new DbContextOptionsBuilder<BookingSystemDbContext>()
            .UseInMemoryDatabase($"booking-tests-{Guid.NewGuid()}")
            .Options;
        var db = new BookingSystemDbContext(options, tenantContext);

        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Tenant { Id = TenantId, Name = "Salone", Timezone = "Europe/Rome", MinCancellationHours = 24 });

        var services = Substitute.For<IServiceRepository>();
        var staff = Substitute.For<IStaffRepository>();
        var bookings = Substitute.For<IBookingRepository>();
        var email = Substitute.For<IEmailService>();

        var sut = new BookingService(db, tenantContext, tenants, services, staff, bookings, email,
            NullLogger<BookingService>.Instance);
        return new Harness(sut, db, bookings, email);
    }

    private static Booking MakeBooking(BookingStatus status, DateOnly date, Guid? staffId = null) => new()
    {
        Id = BookingId,
        TenantId = TenantId,
        ServiceId = ServiceId,
        StaffId = staffId,
        BookingDate = date,
        BookingTime = At10,
        DurationMinutes = 30,
        CustomerName = "Mario Rossi",
        CustomerPhone = "+39 333 0000000",
        CustomerEmail = "mario@example.it",
        Status = status,
        CancellationToken = Token,
        Service = new Service { Id = ServiceId, Name = "Taglio Uomo" },
        Staff = staffId is Guid sid ? new Staff { Id = sid, Name = "Marco" } : null,
    };

    private static void Returns(Harness h, Booking? booking) =>
        h.Bookings.GetByIdAndTokenAsync(BookingId, Token, Arg.Any<CancellationToken>()).Returns(booking);

    // ── GetByTokenAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByToken_id_o_token_errati_ritorna_not_found_neutro()
    {
        Harness h = CreateSut();
        Returns(h, null);

        Result<Core.Dtos.Public.BookingDetailResponse> result = await h.Sut.GetByTokenAsync(BookingId, Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetByToken_mappa_il_dettaglio_e_canCancel_true_entro_il_preavviso()
    {
        Harness h = CreateSut();
        Returns(h, MakeBooking(BookingStatus.Confirmed, FutureDate, StaffId));

        Result<Core.Dtos.Public.BookingDetailResponse> result = await h.Sut.GetByTokenAsync(BookingId, Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(BookingId, result.Value.BookingId);
        Assert.Equal("confirmed", result.Value.Status);
        Assert.Equal("Taglio Uomo", result.Value.Service.Name);
        Assert.NotNull(result.Value.Staff);
        Assert.Equal("Marco", result.Value.Staff!.Name);
        Assert.True(result.Value.CanCancel);
    }

    [Fact]
    public async Task GetByToken_canCancel_false_oltre_il_preavviso()
    {
        Harness h = CreateSut();
        Returns(h, MakeBooking(BookingStatus.Confirmed, PastDate));

        Result<Core.Dtos.Public.BookingDetailResponse> result = await h.Sut.GetByTokenAsync(BookingId, Token);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanCancel);
    }

    [Fact]
    public async Task GetByToken_canCancel_false_se_non_confermata()
    {
        Harness h = CreateSut();
        Returns(h, MakeBooking(BookingStatus.Cancelled, FutureDate));

        Result<Core.Dtos.Public.BookingDetailResponse> result = await h.Sut.GetByTokenAsync(BookingId, Token);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanCancel);
    }

    [Fact]
    public async Task GetByToken_staff_null_se_prenotazione_senza_staff()
    {
        Harness h = CreateSut();
        Returns(h, MakeBooking(BookingStatus.Confirmed, FutureDate, staffId: null));

        Result<Core.Dtos.Public.BookingDetailResponse> result = await h.Sut.GetByTokenAsync(BookingId, Token);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Staff);
    }

    // ── CancelAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_id_o_token_errati_ritorna_not_found_neutro()
    {
        Harness h = CreateSut();
        Returns(h, null);

        Result<Core.Dtos.Public.CancelBookingResponse> result = await h.Sut.CancelAsync(BookingId, Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task Cancel_stato_non_disdicibile_ritorna_422()
    {
        Harness h = CreateSut();
        Returns(h, MakeBooking(BookingStatus.Cancelled, FutureDate));

        Result<Core.Dtos.Public.CancelBookingResponse> result = await h.Sut.CancelAsync(BookingId, Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal("booking_not_cancellable", result.Error.Code);
    }

    [Fact]
    public async Task Cancel_oltre_il_preavviso_ritorna_403()
    {
        Harness h = CreateSut();
        Returns(h, MakeBooking(BookingStatus.Confirmed, PastDate));

        Result<Core.Dtos.Public.CancelBookingResponse> result = await h.Sut.CancelAsync(BookingId, Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.Equal("cancellation_deadline_exceeded", result.Error.Code);
    }

    [Fact]
    public async Task Cancel_entro_il_preavviso_disdice_e_registra_audit_ed_email()
    {
        Harness h = CreateSut();
        Booking booking = MakeBooking(BookingStatus.Confirmed, FutureDate);
        Returns(h, booking);

        Result<Core.Dtos.Public.CancelBookingResponse> result = await h.Sut.CancelAsync(BookingId, Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("cancelled", result.Value.Status);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal("customer", booking.CancellationReason);
        Assert.NotNull(booking.CancelledAt);

        await h.Email.Received(1).SendCancellationConfirmationAsync(booking, Arg.Any<CancellationToken>());

        AuditLog audit = Assert.Single(h.Db.AuditLogs.ToList());
        Assert.Equal("booking_cancelled_by_customer", audit.Action);
        Assert.Equal(BookingId, audit.BookingId);
    }
}
