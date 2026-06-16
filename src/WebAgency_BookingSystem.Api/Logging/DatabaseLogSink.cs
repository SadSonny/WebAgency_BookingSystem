// [INTENT]: Configura il sink Serilog su PostgreSQL (ADDITIVO alla Console). Persiste i log dal livello
// configurato (default Information) in una tabella dedicata (auto-creata), SEPARATA da audit_log. Le scritture
// sono in batch per non penalizzare le richieste e usano una connessione Npgsql propria del sink (non EF),
// quindi l'INSERT dei log non si auto-logga. Disattivabile via config; senza connection string il sink non viene
// aggiunto e resta la sola Console (così non si perdono log durante incidenti del DB).

using NpgsqlTypes;
using Serilog;
using Serilog.Sinks.PostgreSQL;
using Serilog.Sinks.PostgreSQL.ColumnWriters;

namespace WebAgency_BookingSystem.Api.Logging;

internal static class DatabaseLogSink
{
    /// <summary>
    /// Aggiunge il sink PostgreSQL alla configurazione Serilog se abilitato e con connection string valida;
    /// altrimenti restituisce la configurazione invariata.
    /// </summary>
    public static LoggerConfiguration ConfigureDatabaseSink(this LoggerConfiguration logger, DatabaseLogSettings settings)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            return logger;
        }

        var columns = new Dictionary<string, ColumnWriterBase>
        {
            ["timestamp"] = new TimestampColumnWriter(NpgsqlDbType.TimestampTz),
            ["level"] = new LevelColumnWriter(renderAsText: true, NpgsqlDbType.Varchar),
            ["message"] = new RenderedMessageColumnWriter(NpgsqlDbType.Text),
            ["message_template"] = new MessageTemplateColumnWriter(NpgsqlDbType.Text),
            ["exception"] = new ExceptionColumnWriter(NpgsqlDbType.Text),
            ["properties"] = new PropertiesColumnWriter(NpgsqlDbType.Jsonb),
            ["application"] = new SinglePropertyColumnWriter("Application", PropertyWriteMethod.Raw, NpgsqlDbType.Text),
            ["environment"] = new SinglePropertyColumnWriter("Environment", PropertyWriteMethod.Raw, NpgsqlDbType.Text),
            ["request_id"] = new SinglePropertyColumnWriter("RequestId", PropertyWriteMethod.Raw, NpgsqlDbType.Text),
        };

        return logger.WriteTo.PostgreSQL(
            connectionString: settings.ConnectionString,
            tableName: settings.Table,
            columnOptions: columns,
            restrictedToMinimumLevel: settings.MinimumLevel,
            needAutoCreateTable: true,
            schemaName: "public",
            useCopy: true);
    }
}
