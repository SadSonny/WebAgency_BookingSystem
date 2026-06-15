// [INTENT]: Registrazione DI del sottosistema origini CORS per-tenant (PH-1). Crea il TenantOriginCatalog
// (singleton condiviso), registra il refresh job in background e restituisce l'istanza del catalogo, così
// Program.cs può catturarla nella callback CORS (SetIsOriginAllowed) che gira a ogni richiesta.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WebAgency_BookingSystem.Infrastructure.Cors;

/// <summary>
/// Estensione di registrazione del catalogo origini CORS basato sui siteUrl dei tenant.
/// </summary>
public static class CorsOriginRegistration
{
    private const int DefaultRefreshSeconds = 60;
    private const int MinRefreshSeconds = 5;

    /// <summary>
    /// Registra il <see cref="TenantOriginCatalog"/> (singleton) e il job di refresh in background, e
    /// restituisce l'istanza del catalogo da usare nella policy CORS. Intervallo da <c>Cors:OriginRefreshSeconds</c>.
    /// </summary>
    public static TenantOriginCatalog AddTenantCorsOrigins(this IServiceCollection services, IConfiguration configuration)
    {
        var catalog = new TenantOriginCatalog();
        services.AddSingleton(catalog);

        int seconds = configuration.GetValue<int?>("Cors:OriginRefreshSeconds") ?? DefaultRefreshSeconds;
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(seconds, MinRefreshSeconds));

        services.AddHostedService(sp => new TenantOriginRefreshJob(
            catalog,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<TenantOriginRefreshJob>>(),
            interval));

        return catalog;
    }
}
