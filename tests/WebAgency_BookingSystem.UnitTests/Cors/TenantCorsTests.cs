// [INTENT]: Unit test del sottosistema CORS per-tenant (PH-1): normalizzazione siteUrl→origine
// (OriginNormalizer) e match case-insensitive con sostituzione atomica (TenantOriginCatalog). Coprono i
// casi che il browser produce (porte di default omesse, host in lowercase, path ignorato) e gli input invalidi.

using WebAgency_BookingSystem.Infrastructure.Cors;

namespace WebAgency_BookingSystem.UnitTests.Cors;

public class TenantCorsTests
{
    [Theory]
    [InlineData("https://www.salone.it", "https://www.salone.it")]
    [InlineData("https://www.salone.it/prenota", "https://www.salone.it")]       // path ignorato
    [InlineData("https://www.salone.it/prenota?x=1", "https://www.salone.it")]   // query ignorata
    [InlineData("https://WWW.Salone.IT", "https://www.salone.it")]               // host lowercase
    [InlineData("https://salone.it:443", "https://salone.it")]                   // porta default https omessa
    [InlineData("http://localhost:5173", "http://localhost:5173")]              // porta non-default mantenuta
    [InlineData("http://shop.salone.it:8080/", "http://shop.salone.it:8080")]
    public void Normalizza_siteUrl_in_origine(string siteUrl, string expected)
    {
        Assert.Equal(expected, OriginNormalizer.FromSiteUrl(siteUrl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("non-un-url")]
    [InlineData("ftp://salone.it")]      // schema non http/https
    [InlineData("/path/relativo")]
    public void Restituisce_null_su_input_invalido(string? siteUrl)
    {
        Assert.Null(OriginNormalizer.FromSiteUrl(siteUrl));
    }

    [Fact]
    public void Catalogo_vuoto_non_ammette_nulla()
    {
        var catalog = new TenantOriginCatalog();

        Assert.False(catalog.IsAllowed("https://www.salone.it"));
        Assert.Equal(0, catalog.Count);
    }

    [Fact]
    public void Catalogo_ammette_origini_caricate_case_insensitive()
    {
        var catalog = new TenantOriginCatalog();
        catalog.Replace(["https://www.salone.it", "http://localhost:5173"]);

        Assert.True(catalog.IsAllowed("https://www.salone.it"));
        Assert.True(catalog.IsAllowed("https://WWW.SALONE.IT")); // confronto case-insensitive
        Assert.True(catalog.IsAllowed("http://localhost:5173"));
        Assert.False(catalog.IsAllowed("https://altro-sito.it"));
        Assert.Equal(2, catalog.Count);
    }

    [Fact]
    public void Replace_sostituisce_atomicamente_il_set()
    {
        var catalog = new TenantOriginCatalog();
        catalog.Replace(["https://vecchio.it"]);
        catalog.Replace(["https://nuovo.it"]);

        Assert.False(catalog.IsAllowed("https://vecchio.it"));
        Assert.True(catalog.IsAllowed("https://nuovo.it"));
    }

    [Fact]
    public void IsAllowed_su_origine_vuota_e_falso()
    {
        var catalog = new TenantOriginCatalog();
        catalog.Replace(["https://www.salone.it"]);

        Assert.False(catalog.IsAllowed(null));
        Assert.False(catalog.IsAllowed(""));
    }
}
