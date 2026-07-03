// [INTENT]: Normalizza la connection string del database. Le piattaforme PaaS (Railway/Render/Fly/Heroku)
// forniscono DATABASE_URL in formato URI `postgresql://user:pass@host:port/db?sslmode=...`, che Npgsql NON
// accetta (vuole il formato keyword `Host=...;Username=...`). Questo helper converte l'URI in formato keyword;
// una stringa già in formato keyword è restituita invariata. Così l'app "funziona e basta" con qualsiasi
// DATABASE_URL, senza passi manuali di configurazione al deploy.

using Npgsql;

namespace WebAgency_BookingSystem.Infrastructure.Persistence;

internal static class DatabaseConnectionString
{
    private const int DefaultPostgresPort = 5432;

    /// <summary>
    /// Se <paramref name="value"/> è un URI PostgreSQL (`postgres://`/`postgresql://`) lo converte nel formato
    /// keyword di Npgsql; altrimenti lo restituisce invariato (è già una connection string keyword).
    /// </summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        bool isUri = value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
        if (!isUri)
        {
            return value;
        }

        var uri = new Uri(value);
        string[] userInfo = uri.UserInfo.Split(':', 2);

        // WHY: costruiamo l'output con NpgsqlConnectionStringBuilder (non concatenazione) così l'escaping di
        // valori con caratteri speciali (es. password con ';') è gestito correttamente. Username/password vanno
        // url-decodificati perché nell'URI sono percent-encoded.
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : DefaultPostgresPort,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
        };

        // WHY: onoriamo sslmode se presente nell'URI (il proxy pubblico Railway lo richiede; la rete interna no),
        // senza forzarlo quando assente. In Npgsql 10 SslMode.Require cifra SENZA verificare il certificato
        // (TrustServerCertificate è deprecato e inutile), che è il comportamento adatto ai Postgres gestiti.
        string? sslmode = ExtractSslMode(uri.Query);
        if (sslmode is not null && Enum.TryParse(sslmode, ignoreCase: true, out SslMode parsed))
        {
            builder.SslMode = parsed;
        }

        return builder.ConnectionString;
    }

    // WHY: parsing minimale della query senza System.Web (non referenziato). Estrae solo sslmode; normalizza il
    // valore Postgres "verify-full"/"verify-ca" nel nome enum Npgsql (VerifyFull/VerifyCA).
    private static string? ExtractSslMode(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                return kv[1].Replace("-", string.Empty);
            }
        }

        return null;
    }
}
