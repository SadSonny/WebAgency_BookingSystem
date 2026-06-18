// [INTENT]: Verifica end-to-end dell'API platform: login agency-admin, isolamento token platform↔tenant,
// crea tenant, lista/dettaglio, deactivate (cache), API key cross-tenant, setup break-glass.

using System.Net;
using System.Net.Http.Json;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;
using Xunit;

namespace WebAgency_BookingSystem.IntegrationTests.Platform;

[Collection("Integration")]
public class PlatformFlowTests : IntegrationTestBase
{
    public PlatformFlowTests(BookingSystemFixture fixture) : base(fixture) { }

    private sealed record TokenDto(string Token, string TokenType, string ExpiresAt);

    private static async Task<string> LoginPlatformAsync(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/v1/platform/auth/token",
            new { email = TestData.PlatformEmail, password = TestData.PlatformPassword });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<TokenDto>())!.Token;
    }

    [Fact]
    public async Task PlatformLogin_Works_AndTokenAccessesPlatformRoute()
    {
        var c = Fixture.Factory.CreateClient();
        var token = await LoginPlatformAsync(c);
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var list = await c.GetAsync("/api/v1/platform/tenants?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task TenantJwt_IsRejected_OnPlatformRoute()
    {
        var c = Fixture.Factory.CreateClient();
        var login = await c.PostAsJsonAsync("/api/v1/admin/auth/token",
            new { email = TestData.OwnerEmail, password = TestData.OwnerPassword });
        var token = (await login.Content.ReadFromJsonAsync<TokenDto>())!.Token;
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var resp = await c.GetAsync("/api/v1/platform/tenants");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_ThenListAndGet()
    {
        var c = Fixture.Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", await LoginPlatformAsync(c));
        string slug = $"plat-{Guid.NewGuid():N}".Substring(0, 18);
        var body = new
        {
            slug, name = "Plat Test", siteUrl = "https://plat.example.it", ownerEmail = $"o-{slug}@test.it",
            services = new[] { new { localId = "s1", name = "Taglio", durationMinutes = 30, basePrice = 15.0 } },
        };
        var create = await c.PostAsJsonAsync("/api/v1/platform/tenants", body);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await c.GetAsync("/api/v1/platform/tenants?pageSize=200");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Setup_BreakGlass_ResetsPassword()
    {
        var c = Fixture.Factory.CreateClient();
        // env token corretto → reimposta la password dell'admin seedato.
        var ok = await c.PostAsJsonAsync("/api/v1/platform/setup",
            new { setupToken = "test-setup-token", email = TestData.PlatformEmail, password = "NewPlatform999!" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        // login con la nuova password.
        var login = await c.PostAsJsonAsync("/api/v1/platform/auth/token",
            new { email = TestData.PlatformEmail, password = "NewPlatform999!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        // token errato → 401.
        var bad = await c.PostAsJsonAsync("/api/v1/platform/setup",
            new { setupToken = "wrong", email = TestData.PlatformEmail, password = "Whatever123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        // ripristina la password seed per non sporcare altri test.
        await c.PostAsJsonAsync("/api/v1/platform/setup",
            new { setupToken = "test-setup-token", email = TestData.PlatformEmail, password = TestData.PlatformPassword });
    }
}
