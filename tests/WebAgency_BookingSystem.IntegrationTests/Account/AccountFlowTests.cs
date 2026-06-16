// [INTENT]: Test di integrazione del flusso account admin (login, attivazione, cambio e reset password) su
// PostgreSQL reale. I test che mutano una password creano SEMPRE un utente usa-e-getta (email univoca) per
// non contaminare la cache di security stamp condivisa dal factory: solo il login read-only usa l'Owner seminato.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Core.Security;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.IntegrationTests.Account;

[Collection("Integration")]
public class AccountFlowTests : IntegrationTestBase
{
    public AccountFlowTests(BookingSystemFixture fixture) : base(fixture) { }

    private sealed record TokenDto(string Token, string TokenType, string ExpiresAt);

    // ── Helpers ──────────────────────────────────────────────────────────────

    // WHY: ogni utente usa-e-getta ha email e Id unici → i test mutano la propria password senza
    // toccare lo stamp dell'Owner seminato (la cui versione resta cached nel factory condiviso).
    private static string UniqueEmail() => $"u-{Guid.NewGuid():N}@test.it";

    /// <summary>Crea un utente attivato con password nota e ritorna (id, email, password in chiaro).</summary>
    private async Task<(Guid Id, string Email, string Password)> CreateActivatedUserAsync(string password)
    {
        Guid id = Guid.NewGuid();
        string email = UniqueEmail();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        db.Users.Add(new User
        {
            Id = id, TenantId = TestData.TenantId, Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            ActivatedAt = now, SecurityStamp = Guid.NewGuid(),
            Role = UserRole.Owner, Active = true, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (id, email, password);
    }

    /// <summary>Crea un utente NON attivato (PasswordHash null) e ritorna il suo Id e email.</summary>
    private async Task<(Guid Id, string Email)> CreateInactiveUserAsync()
    {
        Guid id = Guid.NewGuid();
        string email = UniqueEmail();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        db.Users.Add(new User
        {
            Id = id, TenantId = TestData.TenantId, Email = email,
            PasswordHash = null, ActivatedAt = null, SecurityStamp = Guid.NewGuid(),
            Role = UserRole.Owner, Active = true, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (id, email);
    }

    /// <summary>Inserisce un token di sicurezza per l'utente e ritorna il token in chiaro.</summary>
    private async Task<string> AddSecurityTokenAsync(Guid userId, SecurityTokenPurpose purpose, TimeSpan validFor)
    {
        GeneratedSecurityToken generated = SecurityTokenGenerator.Generate();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
        db.UserSecurityTokens.Add(new UserSecurityToken
        {
            Id = Guid.NewGuid(), TenantId = TestData.TenantId, UserId = userId,
            TokenHash = generated.TokenHash, Purpose = purpose,
            ExpiresAt = now.Add(validFor), CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return generated.Token;
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/v1/admin/auth/token", new { email, password });

    // ── Test ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithSeededOwner_ReturnsToken()
    {
        HttpClient client = Fixture.Factory.CreateClient();

        HttpResponseMessage response = await LoginAsync(client, TestData.OwnerEmail, TestData.OwnerPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        TokenDto? body = await response.Content.ReadFromJsonAsync<TokenDto>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        HttpClient client = Fixture.Factory.CreateClient();

        HttpResponseMessage response = await LoginAsync(client, TestData.OwnerEmail, "WrongPassword999!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Activation_WithValidToken_SetsPassword_AndRejectsReuse()
    {
        (Guid id, string email) = await CreateInactiveUserAsync();
        string token = await AddSecurityTokenAsync(id, SecurityTokenPurpose.Activation, TimeSpan.FromHours(72));
        const string newPassword = "Activated123!";

        HttpClient client = Fixture.Factory.CreateClient();

        HttpResponseMessage first = await client.PostAsJsonAsync(
            "/api/v1/admin/account/activate", new { token, newPassword });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // WHY: il token monouso ormai consumato è respinto dal servizio con Error.Validation("token_non_valido")
        // → 422 (mappa di ResultMapping). Non è un 400: la richiesta è ben formata, è il token a non essere valido.
        HttpResponseMessage reuse = await client.PostAsJsonAsync(
            "/api/v1/admin/account/activate", new { token, newPassword });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reuse.StatusCode);

        // L'utente ora può autenticarsi con la nuova password.
        HttpResponseMessage login = await LoginAsync(client, email, newPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_InvalidatesOldToken()
    {
        const string known = "KnownPass123!";
        (_, string email, _) = await CreateActivatedUserAsync(known);

        HttpClient client = Fixture.Factory.CreateClient();

        // Login → cattura token JWT.
        HttpResponseMessage loginResp = await LoginAsync(client, email, known);
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        TokenDto? login = await loginResp.Content.ReadFromJsonAsync<TokenDto>();
        Assert.NotNull(login);
        string oldJwt = login!.Token;

        // Cambio password autenticato.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldJwt);
        HttpResponseMessage change = await client.PostAsJsonAsync(
            "/api/v1/admin/account/password",
            new { currentPassword = known, newPassword = "BrandNewPass456!" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // Il vecchio JWT (stamp ormai invalidato) viene respinto su una rotta admin protetta.
        HttpResponseMessage protectedCall = await client.GetAsync("/api/v1/admin/api-keys");
        Assert.Equal(HttpStatusCode.Unauthorized, protectedCall.StatusCode);

        // La nuova password funziona.
        HttpClient fresh = Fixture.Factory.CreateClient();
        HttpResponseMessage relogin = await LoginAsync(fresh, email, "BrandNewPass456!");
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    [Fact]
    public async Task ResetRequest_IsNeutral_ForUnknownEmail()
    {
        HttpClient client = Fixture.Factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/admin/account/password/reset-request", new { email = "nobody@nowhere.it" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task PasswordReset_WithValidToken_SetsNewPassword()
    {
        (Guid id, string email, _) = await CreateActivatedUserAsync("OriginalPass123!");
        string token = await AddSecurityTokenAsync(id, SecurityTokenPurpose.PasswordReset, TimeSpan.FromHours(1));
        const string newPassword = "ResetPass789!";

        HttpClient client = Fixture.Factory.CreateClient();

        HttpResponseMessage reset = await client.PostAsJsonAsync(
            "/api/v1/admin/account/password/reset", new { token, newPassword });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        HttpResponseMessage login = await LoginAsync(client, email, newPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
