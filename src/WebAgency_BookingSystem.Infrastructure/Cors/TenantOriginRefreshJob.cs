// [INTENT]: BackgroundService che ricarica periodicamente le origini CORS ammesse nel TenantOriginCatalog,
// derivandole dai siteUrl dei tenant attivi (PH-1). Il refresh in background evita query DB sul path di
// richiesta. L'intervallo è configurabile (Cors:OriginRefreshSeconds, default 60s): un nuovo tenant
// provisionato via CLI diventa "ammesso" entro un intervallo, senza modifiche di config né riavvii.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;

namespace WebAgency_BookingSystem.Infrastructure.Cors;

internal sealed class TenantOriginRefreshJob : BackgroundService
{
    private readonly TenantOriginCatalog _catalog;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantOriginRefreshJob> _logger;
    private readonly TimeSpan _interval;

    public TenantOriginRefreshJob(
        TenantOriginCatalog catalog,
        IServiceScopeFactory scopeFactory,
        ILogger<TenantOriginRefreshJob> logger,
        TimeSpan interval)
    {
        _catalog = catalog;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // WHY: primo caricamento immediato all'avvio, poi a intervalli regolari. Finché il primo refresh non
        // completa, il catalogo è vuoto → nessuna origine cross-site ammessa (default sicuro, non permissivo).
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // WHY: un errore transitorio del DB non deve far morire il job; manteniamo lo snapshot
                // precedente e riproviamo al tick successivo.
                _logger.LogWarning(ex, "Refresh origini CORS fallito; mantengo lo snapshot precedente ({Count} origini).", _catalog.Count);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();

        IReadOnlyList<string> siteUrls = await tenants.GetActiveSiteUrlsAsync(ct);
        var origins = siteUrls
            .Select(OriginNormalizer.FromSiteUrl)
            .Where(o => o is not null)
            .Select(o => o!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _catalog.Replace(origins);
        _logger.LogDebug("Origini CORS aggiornate: {Count} origini da {TenantCount} tenant attivi.", origins.Count, siteUrls.Count);
    }
}
