// [INTENT]: Test di integrazione per la pipeline middleware. Verifica i comportamenti di sicurezza
// osservabili via HTTP: 401 senza API key, 403 con chiave invalida, header X-Trace-Id sempre presente,
// 400 su body malformato con envelope corretto. Non richiede cleanup perché non crea prenotazioni.

using System.Net;
using System.Text;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Middleware;

public sealed class PipelineTests : IntegrationTestBase
{
    public PipelineTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task senza_api_key_su_endpoint_tenant_scoped_restituisce_401_unauthorized()
    {
        using var client = Fixture.Factory.CreateClient();

        var response = await client.GetAsync("/api/v1/services");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"unauthorized\"", body);
    }

    [Fact]
    public async Task api_key_invalida_restituisce_403_forbidden()
    {
        using var client = Fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "chiave-non-esistente-nel-db");

        var response = await client.GetAsync("/api/v1/services");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"forbidden\"", body);
    }

    [Fact]
    public async Task ogni_risposta_ha_header_x_trace_id()
    {
        using var client = Fixture.Factory.CreateClient();

        // WHY: il correlation middleware aggiunge X-Trace-Id su tutte le risposte,
        // incluse quelle 401/403 che escono prima della tenant resolution.
        var response = await client.GetAsync("/api/v1/health");

        Assert.True(response.Headers.Contains("X-Trace-Id"),
            "X-Trace-Id deve essere presente in ogni risposta (R-02).");
    }

    [Fact]
    public async Task post_con_json_malformato_restituisce_400_con_code_bad_request()
    {
        using var client = AuthenticatedClient();
        var malformed = new StringContent("{ non-valid-json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/bookings", malformed);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"bad_request\"", body);
    }
}
