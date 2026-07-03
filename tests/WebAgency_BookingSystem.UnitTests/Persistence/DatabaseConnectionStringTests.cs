// [INTENT]: Unit test del convertitore DATABASE_URL. Le piattaforme PaaS (Railway/Render/Fly/Heroku) forniscono
// la connection string in formato URI `postgresql://user:pass@host:port/db`, che Npgsql NON accetta: serve il
// formato keyword `Host=...;Username=...`. I test verificano la conversione (host/porta/db/credenziali, password
// url-encoded, sslmode), il pass-through di una stringa già in formato keyword, e documentano che Npgsql rifiuta
// l'URI grezzo (la ragione d'essere del convertitore).

using Npgsql;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.UnitTests.Persistence;

public class DatabaseConnectionStringTests
{
    [Fact]
    public void uri_completo_convertito_in_keyword()
    {
        string result = DatabaseConnectionString.Normalize("postgresql://bob:secret@db.host:5433/appdb");

        var b = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal("db.host", b.Host);
        Assert.Equal(5433, b.Port);
        Assert.Equal("appdb", b.Database);
        Assert.Equal("bob", b.Username);
        Assert.Equal("secret", b.Password);
    }

    [Fact]
    public void schema_postgres_breve_supportato()
    {
        string result = DatabaseConnectionString.Normalize("postgres://bob:secret@db.host/appdb");

        var b = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal("db.host", b.Host);
        Assert.Equal("appdb", b.Database);
    }

    [Fact]
    public void uri_senza_porta_usa_5432()
    {
        string result = DatabaseConnectionString.Normalize("postgresql://bob:secret@db.host/appdb");

        Assert.Equal(5432, new NpgsqlConnectionStringBuilder(result).Port);
    }

    [Fact]
    public void password_url_encoded_viene_decodificata()
    {
        // La password reale è "p@ss:w/d" → url-encoded nell'URI.
        string result = DatabaseConnectionString.Normalize("postgresql://bob:p%40ss%3Aw%2Fd@db.host/appdb");

        Assert.Equal("p@ss:w/d", new NpgsqlConnectionStringBuilder(result).Password);
    }

    [Fact]
    public void sslmode_require_mappato()
    {
        // WHY: in Npgsql 10 SslMode.Require cifra senza verificare il certificato (TrustServerCertificate è
        // deprecato e non fa nulla) — adatto ai Postgres gestiti (Railway/Render).
        string result = DatabaseConnectionString.Normalize("postgresql://bob:secret@db.host/appdb?sslmode=require");

        Assert.Equal(SslMode.Require, new NpgsqlConnectionStringBuilder(result).SslMode);
    }

    [Fact]
    public void stringa_keyword_passthrough_invariata()
    {
        const string keyword = "Host=localhost;Port=5432;Database=x;Username=u;Password=p";

        Assert.Equal(keyword, DatabaseConnectionString.Normalize(keyword));
    }

    [Fact]
    public void null_o_vuoto_passthrough()
    {
        Assert.Equal("", DatabaseConnectionString.Normalize(""));
    }

    [Fact]
    public void npgsql_rifiuta_uri_grezzo_percio_serve_il_convertitore()
    {
        // WHY: documenta la ragione d'essere del convertitore. Se un giorno Npgsql accettasse l'URI, questo
        // test fallirebbe segnalando che il convertitore non è più necessario.
        Assert.ThrowsAny<Exception>(() =>
        {
            var b = new NpgsqlConnectionStringBuilder("postgresql://bob:secret@db.host:5432/appdb");
            _ = b.Host; // forza la valutazione
        });
    }
}
