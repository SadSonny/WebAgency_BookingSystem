// [INTENT]: Implementazione di ILogErrorSource: legge gli errori dalla tabella dei log applicativi (sink Serilog,
// NON mappata in EF) via SQL grezzo. Singleton che apre uno scope per chiamata. Il nome tabella è una whitelist
// validata, iniettata in DI (vedi DependencyInjection); i valori (watermark, livelli) sono sempre parametrici.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Observability;
using WebAgency_BookingSystem.Infrastructure.Persistence;

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal sealed class DbLogErrorSource : ILogErrorSource
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _logTable;

    public DbLogErrorSource(IServiceScopeFactory scopeFactory, string logTable)
    {
        _scopeFactory = scopeFactory;
        _logTable = logTable;
    }

    public async Task<IReadOnlyList<LogError>> GetSinceAsync(
        DateTimeOffset since, IReadOnlyList<string> levels, CancellationToken ct = default)
    {
        // WHY: un insieme di livelli vuoto produrrebbe "= ANY('{}')", il cui handling Npgsql può variare; evitiamo la
        // query del tutto (non c'è nulla da cercare).
        if (levels.Count == 0)
        {
            return Array.Empty<LogError>();
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        BookingSystemDbContext db = scope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();

        // WHY: il nome tabella è una whitelist validata (DatabaseLogSettings) → concatenarlo come identificatore è
        // sicuro; i valori restano parametrici ({0} watermark, {1} array di livelli con Postgres ANY). Stringa non
        // interpolata per non incorrere nell'analyzer EF1002.
        string sql =
            "SELECT \"timestamp\" AS \"Timestamp\", \"level\" AS \"Level\", \"message\" AS \"Message\" FROM "
            + _logTable
            + " WHERE \"timestamp\" > {0} AND \"level\" = ANY({1}) ORDER BY \"timestamp\"";

        List<LogError> rows = await db.Database
            .SqlQueryRaw<LogError>(sql, since, levels.ToArray())
            .ToListAsync(ct);

        return rows;
    }
}
