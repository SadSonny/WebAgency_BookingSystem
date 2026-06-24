// [INTENT]: Integration test di DbLogErrorSource (Monitor OPS 4.2) contro PostgreSQL reale. È l'unico punto che
// valida la SQL grezza dell'OPS — mai eseguita con successo dagli altri test (nei test il sink DB è disattivato,
// quindi la tabella `logs` non esiste). Crea una tabella `logs` reale, inserisce righe e verifica che la query
// filtri per livello e watermark e mappi correttamente le colonne (nomi colonna, ANY(text[]), alias → LogError).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Observability;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.IntegrationTests.Fixtures;

namespace WebAgency_BookingSystem.IntegrationTests.Ops;

[Collection("Integration")]
public class OpsLogErrorSourceTests : IntegrationTestBase
{
    public OpsLogErrorSourceTests(BookingSystemFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetSince_FiltraPerLivelloEWatermark_SuPostgresReale()
    {
        var watermark = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using (var scope = Fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS logs (\"timestamp\" timestamptz NOT NULL, \"level\" varchar(50) NOT NULL, \"message\" text NULL)");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM logs");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO logs (\"timestamp\",\"level\",\"message\") VALUES ({0},{1},{2}),({3},{4},{5}),({6},{7},{8}),({9},{10},{11})",
                watermark.AddMinutes(1), "Error", "boom error",        // incluso
                watermark.AddMinutes(2), "Fatal", "boom fatal",        // incluso
                watermark.AddMinutes(3), "Information", "solo info",   // escluso: sotto MinLevel
                watermark.AddMinutes(-5), "Error", "vecchio error");   // escluso: prima del watermark
        }

        var source = Fixture.Factory.Services.GetRequiredService<ILogErrorSource>();

        try
        {
            IReadOnlyList<LogError> result = await source.GetSinceAsync(
                watermark, new[] { "Error", "Fatal" }, CancellationToken.None);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, r => r.Level == "Error" && r.Message == "boom error");
            Assert.Contains(result, r => r.Level == "Fatal" && r.Message == "boom fatal");
            Assert.DoesNotContain(result, r => r.Message == "solo info");      // filtro livello
            Assert.DoesNotContain(result, r => r.Message == "vecchio error");  // filtro watermark
            // Ordinamento crescente per timestamp.
            Assert.True(result[0].Timestamp <= result[1].Timestamp);
        }
        finally
        {
            using var scope = Fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS logs");
        }
    }

    [Fact]
    public async Task GetSince_LivelliVuoti_RitornaListaVuota()
    {
        var source = Fixture.Factory.Services.GetRequiredService<ILogErrorSource>();

        IReadOnlyList<LogError> result = await source.GetSinceAsync(
            DateTimeOffset.UtcNow.AddDays(-1), Array.Empty<string>(), CancellationToken.None);

        Assert.Empty(result);
    }
}
