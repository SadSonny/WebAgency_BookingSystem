// [INTENT]: Unit test del servizio DSAR (GDPR 4.3) con EF InMemory + ITenantContext fake. Coprono export (match,
// tutti i campi, isolamento tenant, vuoto=successo, case-insensitive), erase (anonimizza bookings, elimina outbox,
// idempotenza, 404), e l'audit PII-free (subjectRef = HMAC, niente email in chiaro).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Services;

namespace WebAgency_BookingSystem.UnitTests.Services;

public class GdprDsarServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = "test-secret-key-at-least-32-chars-long!!" })
        .Build();

    private static BookingSystemDbContext NewDb(Guid tenantId, InMemoryDatabaseRoot root, string name)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        DbContextOptions<BookingSystemDbContext> options = new DbContextOptionsBuilder<BookingSystemDbContext>()
            .UseInMemoryDatabase(name, root)
            .Options;
        return new BookingSystemDbContext(options, tenantContext);
    }

    private static Booking Booking(Guid tenantId, string email, string name = "Mario Rossi", BookingStatus status = BookingStatus.Confirmed) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ServiceId = Guid.NewGuid(),
        BookingDate = new DateOnly(2035, 1, 1),
        BookingTime = new TimeOnly(10, 0),
        DurationMinutes = 30,
        CustomerName = name,
        CustomerPhone = "+39 333 0000000",
        CustomerEmail = email,
        CustomerNotes = "note",
        GdprConsent = true,
        GdprConsentAt = DateTimeOffset.UtcNow,
        GdprConsentVersion = "2026-06-01",
        Status = status,
        CancellationToken = Guid.NewGuid(),
    };

    // Helper: crea un servizio il cui DbContext è bound a TenantA, condividendo il root con i seed.
    private static (GdprDsarService sut, BookingSystemDbContext queryDb) SutForTenantA(InMemoryDatabaseRoot root, string name)
    {
        BookingSystemDbContext db = NewDb(TenantA, root, name);
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TenantA);
        var sut = new GdprDsarService(db, tenantContext, Config(), NullLogger<GdprDsarService>.Instance);
        return (sut, db);
    }

    [Fact]
    public async Task export_restituisce_le_prenotazioni_del_cliente_con_tutti_i_campi()
    {
        var root = new InMemoryDatabaseRoot();
        string name = $"gdpr-{Guid.NewGuid()}";
        using (BookingSystemDbContext seed = NewDb(TenantA, root, name))
        {
            seed.Bookings.Add(Booking(TenantA, "mario@example.it"));
            seed.Bookings.Add(Booking(TenantA, "altro@example.it"));
            await seed.SaveChangesAsync();
        }
        (GdprDsarService sut, BookingSystemDbContext _) = SutForTenantA(root, name);

        Result<CustomerDataExport> result = await sut.ExportAsync("mario@example.it");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Count);
        BookingExportItem item = Assert.Single(result.Value.Bookings);
        Assert.Equal("mario@example.it", item.CustomerEmail);
        Assert.Equal("2026-06-01", item.GdprConsentVersion);
    }

    [Fact]
    public async Task export_isolamento_tenant_non_vede_dati_di_altri_tenant()
    {
        var root = new InMemoryDatabaseRoot();
        string name = $"gdpr-{Guid.NewGuid()}";
        using (BookingSystemDbContext seedB = NewDb(TenantB, root, name))
        {
            seedB.Bookings.Add(Booking(TenantB, "mario@example.it"));
            await seedB.SaveChangesAsync();
        }
        (GdprDsarService sut, BookingSystemDbContext _) = SutForTenantA(root, name);

        Result<CustomerDataExport> result = await sut.ExportAsync("mario@example.it");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Count); // i dati di TenantB sono nascosti dal global query filter
    }

    [Fact]
    public async Task export_vuoto_e_successo_non_404_e_scrive_audit()
    {
        var root = new InMemoryDatabaseRoot();
        (GdprDsarService sut, BookingSystemDbContext db) = SutForTenantA(root, $"gdpr-{Guid.NewGuid()}");

        Result<CustomerDataExport> result = await sut.ExportAsync("nessuno@example.it");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Bookings);
        AuditLog audit = Assert.Single(db.AuditLogs.IgnoreQueryFilters().ToList());
        Assert.Equal("customer_data_exported", audit.Action);
        Assert.DoesNotContain("nessuno@example.it", audit.Metadata); // niente email in chiaro
    }

    [Fact]
    public async Task export_email_case_insensitive()
    {
        var root = new InMemoryDatabaseRoot();
        string name = $"gdpr-{Guid.NewGuid()}";
        using (BookingSystemDbContext seed = NewDb(TenantA, root, name))
        {
            seed.Bookings.Add(Booking(TenantA, "mario@example.it"));
            await seed.SaveChangesAsync();
        }
        (GdprDsarService sut, BookingSystemDbContext _) = SutForTenantA(root, name);

        Result<CustomerDataExport> result = await sut.ExportAsync("  MARIO@Example.IT ");

        Assert.Equal(1, result.Value.Count);
    }

    [Fact]
    public async Task erase_anonimizza_bookings_ed_elimina_outbox()
    {
        var root = new InMemoryDatabaseRoot();
        string name = $"gdpr-{Guid.NewGuid()}";
        Guid bookingId;
        using (BookingSystemDbContext seed = NewDb(TenantA, root, name))
        {
            Booking b = Booking(TenantA, "mario@example.it");
            bookingId = b.Id;
            seed.Bookings.Add(b);
            seed.OutboxEmails.Add(new OutboxEmail
            {
                Id = Guid.NewGuid(), TenantId = TenantA, Kind = EmailKind.BookingConfirmation,
                Status = OutboxEmailStatus.Sent, ToEmail = "mario@example.it", ToName = "Mario",
                Subject = "Conferma", HtmlBody = "<p>Mario</p>", TextBody = "Mario",
            });
            await seed.SaveChangesAsync();
        }
        (GdprDsarService sut, BookingSystemDbContext db) = SutForTenantA(root, name);

        Result<ErasureResult> result = await sut.EraseAsync("mario@example.it");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AnonymizedBookings);
        Assert.Equal(1, result.Value.PurgedOutbox);
        Booking after = await db.Bookings.IgnoreQueryFilters().SingleAsync(b => b.Id == bookingId);
        Assert.Equal("[rimosso]", after.CustomerName);
        Assert.Equal(string.Empty, after.CustomerEmail);
        Assert.Null(after.CustomerNotes);
        Assert.Empty(db.OutboxEmails.IgnoreQueryFilters().ToList());
        Assert.Contains(db.AuditLogs.IgnoreQueryFilters().ToList(), a => a.Action == "customer_data_erased");
    }

    [Fact]
    public async Task erase_idempotente_seconda_volta_404()
    {
        var root = new InMemoryDatabaseRoot();
        string name = $"gdpr-{Guid.NewGuid()}";
        using (BookingSystemDbContext seed = NewDb(TenantA, root, name))
        {
            seed.Bookings.Add(Booking(TenantA, "mario@example.it"));
            await seed.SaveChangesAsync();
        }
        (GdprDsarService sut1, BookingSystemDbContext _) = SutForTenantA(root, name);
        await sut1.EraseAsync("mario@example.it");

        (GdprDsarService sut2, BookingSystemDbContext _) = SutForTenantA(root, name);
        Result<ErasureResult> second = await sut2.EraseAsync("mario@example.it");

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.NotFound, second.Error.Type);
    }

    [Fact]
    public async Task erase_senza_dati_404_nessun_audit()
    {
        var root = new InMemoryDatabaseRoot();
        (GdprDsarService sut, BookingSystemDbContext db) = SutForTenantA(root, $"gdpr-{Guid.NewGuid()}");

        Result<ErasureResult> result = await sut.EraseAsync("nessuno@example.it");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(db.AuditLogs.IgnoreQueryFilters().ToList());
    }
}
