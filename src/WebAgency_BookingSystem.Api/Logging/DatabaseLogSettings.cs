// [INTENT]: Impostazioni del sink Serilog su PostgreSQL e della relativa retention. Lette dalla sezione
// "DatabaseLogging" (con default sensati). Condivise tra la configurazione del sink (Program.cs) e il job di
// retention, così la fonte di verità è una sola. La connection string segue la stessa priorità del resto
// dell'app: DATABASE_URL → ConnectionStrings:Database.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Serilog.Events;

namespace WebAgency_BookingSystem.Api.Logging;

/// <summary>Configurazione del logging su database (sink PostgreSQL + retention).</summary>
public sealed partial record DatabaseLogSettings(
    bool Enabled,
    string? ConnectionString,
    string Table,
    LogEventLevel MinimumLevel,
    int RetentionDays)
{
    /// <summary>Costruisce le impostazioni dalla configurazione, con default: abilitato, Information, tabella "logs", 90 giorni.</summary>
    public static DatabaseLogSettings FromConfiguration(IConfiguration configuration)
    {
        bool enabled = configuration.GetValue<bool?>("DatabaseLogging:Enabled") ?? true;
        string? conn = configuration["DATABASE_URL"] ?? configuration.GetConnectionString("Database");

        string table = configuration["DatabaseLogging:Table"] is { Length: > 0 } t ? t : "logs";
        // WHY: il nome tabella finisce in SQL come IDENTIFICATORE (non parametrizzabile) nel job di retention.
        // Lo validiamo con una whitelist per escludere a monte qualsiasi tentativo di injection; in caso di
        // valore non conforme ripieghiamo sul default sicuro.
        if (!TableNameRegex().IsMatch(table))
        {
            table = "logs";
        }

        LogEventLevel level = Enum.TryParse(configuration["DatabaseLogging:MinimumLevel"], ignoreCase: true, out LogEventLevel parsed)
            ? parsed
            : LogEventLevel.Information;

        int retentionDays = configuration.GetValue<int?>("DatabaseLogging:RetentionDays") ?? 90;

        return new DatabaseLogSettings(enabled, conn, table, level, retentionDays);
    }

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex TableNameRegex();
}
