// [INTENT]: Integration test del DSAR (GDPR 4.3) su PostgreSQL reale via HTTP. Validano ciò che gli unit test su
// EF InMemory NON possono: traduzione SQL reale (.ToLower→LOWER, case-insensitive vero), atomicità di erase
// (bookings + outbox + audit in un solo SaveChanges su Postgres), autorizzazione admin (401), 404/422,
// serializzazione JSON della response, e audit PII-free (nessuna email in chiaro nei metadata).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;
using BookingEntity = WebAgency_BookingSystem.Core.Entities.Booking;

namespace WebAgency_BookingSystem.IntegrationTests.Gdpr;

[Collection("Integration")]
public class GdprDsarTests : IntegrationTestBase
{
    public GdprDsarTests(BookingSystemFixture fixture) : base(fixture) { }

    private sealed record TokenDto(string Token, string TokenType, string ExpiresAt);
    private sealed record ExportDto(string Email, int Count, List<ExportItemDto> Bookings);
    private sealed record ExportItemDto(string CustomerEmail, string CustomerName, string? GdprConsentVersion);
    private sealed record ErasureDto(int AnonymizedBookings, int PurgedOutbox);

    private async Task<HttpClient> AdminClientAsync()
    {
        HttpClient client = Fixture.Factory.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync("/api/v1/admin/auth/token",
            new { email = TestData.OwnerEmail, password = TestData.OwnerPassword });
        login.EnsureSuccessStatusCode();
        TokenDto token = (await login.Content.ReadFromJsonAsync<TokenDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    private async Task SeedBookingAsync(string email, string? consentVersion = "2026-06-01", bool withOutbox = false)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.Bookings.Add(new BookingEntity
        {
            Id = Guid.NewGuid(), TenantId = TestData.TenantId, ServiceId = TestData.ServiceSingleId,
            BookingDate = TestData.FutureMonday, BookingTime = new TimeOnly(10, 0), DurationMinutes = 30,
            CustomerName = "Mario Rossi", CustomerPhone = "+39 333 0000000", CustomerEmail = email,
            CustomerNotes = "note riservate", GdprConsent = true, GdprConsentAt = now,
            GdprConsentVersion = consentVersion, Status = BookingStatus.Confirmed,
            CancellationToken = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now,
        });
        if (withOutbox)
        {
            db.OutboxEmails.Add(new OutboxEmail
            {
                Id = Guid.NewGuid(), TenantId = TestData.TenantId, Kind = EmailKind.BookingConfirmation,
                Status = OutboxEmailStatus.Sent, ToEmail = email, ToName = "Mario",
                Subject = "Conferma", HtmlBody = "<p>Mario Rossi</p>", TextBody = "Mario Rossi",
                Attempts = 1, NextAttemptAt = now, SentAt = now, CreatedAt = now, UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync();
    }

    private static string ExportUrl(string email) => $"/api/v1/admin/gdpr/customer?email={Uri.EscapeDataString(email)}";

    [Fact]
    public async Task Export_SenzaAuth_Ritorna401()
    {
        HttpClient anon = Fixture.Factory.CreateClient();

        HttpResponseMessage response = await anon.GetAsync(ExportUrl("chiunque@example.it"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Export_RestituisceLeProprieprenotazioni()
    {
        await CleanupBookingsAsync();
        string email = $"mario-{Guid.NewGuid():N}@example.it";
        await SeedBookingAsync(email);
        HttpClient admin = await AdminClientAsync();

        HttpResponseMessage response = await admin.GetAsync(ExportUrl(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ExportDto body = (await response.Content.ReadFromJsonAsync<ExportDto>())!;
        Assert.Equal(1, body.Count);
        Assert.Equal(email, body.Bookings[0].CustomerEmail);
        Assert.Equal("2026-06-01", body.Bookings[0].GdprConsentVersion);
    }

    [Fact]
    public async Task Export_EmailSconosciuta_Ritorna200Vuoto()
    {
        HttpClient admin = await AdminClientAsync();

        HttpResponseMessage response = await admin.GetAsync(ExportUrl($"nessuno-{Guid.NewGuid():N}@example.it"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ExportDto body = (await response.Content.ReadFromJsonAsync<ExportDto>())!;
        Assert.Equal(0, body.Count);
        Assert.Empty(body.Bookings);
    }

    [Fact]
    public async Task Export_EmailCaseInsensitive_SuPostgresReale()
    {
        await CleanupBookingsAsync();
        string email = $"mixedcase-{Guid.NewGuid():N}@example.it";
        await SeedBookingAsync(email.ToLowerInvariant());
        HttpClient admin = await AdminClientAsync();

        // Interroga con MAIUSCOLE + spazi: deve matchare grazie a LOWER()/Trim su Postgres.
        HttpResponseMessage response = await admin.GetAsync(ExportUrl("  " + email.ToUpperInvariant() + "  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ExportDto body = (await response.Content.ReadFromJsonAsync<ExportDto>())!;
        Assert.Equal(1, body.Count);
    }

    [Fact]
    public async Task Erase_AnonimizzaBookings_EliminaOutbox_ScriveAudit()
    {
        await CleanupBookingsAsync();
        string email = $"erase-{Guid.NewGuid():N}@example.it";
        await SeedBookingAsync(email, withOutbox: true);
        HttpClient admin = await AdminClientAsync();

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/v1/admin/gdpr/customer/erase", new { email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ErasureDto body = (await response.Content.ReadFromJsonAsync<ErasureDto>())!;
        Assert.Equal(1, body.AnonymizedBookings);
        Assert.Equal(1, body.PurgedOutbox);

        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        // Le prenotazioni del cliente risultano anonimizzate; l'outbox eliminata.
        BookingEntity anonymized = await db.Bookings.IgnoreQueryFilters().SingleAsync(b => b.CustomerNotes == null && b.CustomerName == "[rimosso]");
        Assert.Equal(string.Empty, anonymized.CustomerEmail);
        Assert.False(await db.OutboxEmails.IgnoreQueryFilters().AnyAsync(e => e.ToEmail == email));
        // L'audit registra la cancellazione SENZA l'email in chiaro.
        List<AuditLog> audits = await db.AuditLogs.IgnoreQueryFilters().Where(a => a.Action == "customer_data_erased").ToListAsync();
        Assert.NotEmpty(audits);
        Assert.All(audits, a => Assert.DoesNotContain(email, a.Metadata ?? string.Empty));
    }

    [Fact]
    public async Task Erase_EmailSconosciuta_Ritorna404()
    {
        await CleanupBookingsAsync();
        HttpClient admin = await AdminClientAsync();

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/v1/admin/gdpr/customer/erase", new { email = $"nessuno-{Guid.NewGuid():N}@example.it" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Erase_EmailInvalida_Ritorna422()
    {
        HttpClient admin = await AdminClientAsync();

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/v1/admin/gdpr/customer/erase", new { email = "non-una-email" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
