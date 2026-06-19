// [INTENT]: Punto unico di registrazione DI del layer Infrastructure. Configura il DbContext (Npgsql +
// snake_case), il TenantContext scoped, i repository e l'implementazione email. Chiamato da Program.cs.
// La connection string si legge da DATABASE_URL (priorità) o dalla sezione ConnectionStrings:Database.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Observability;
using WebAgency_BookingSystem.Infrastructure.Auth;
using WebAgency_BookingSystem.Infrastructure.Email;
using WebAgency_BookingSystem.Infrastructure.Observability;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Persistence.Caching;
using WebAgency_BookingSystem.Infrastructure.Persistence.Interceptors;
using WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;
using WebAgency_BookingSystem.Infrastructure.Services.Admin;
using WebAgency_BookingSystem.Infrastructure.Services;
using WebAgency_BookingSystem.Infrastructure.Services.Platform;
using WebAgency_BookingSystem.Infrastructure.Services.Provisioning;
using WebAgency_BookingSystem.Infrastructure.Tenancy;

namespace WebAgency_BookingSystem.Infrastructure;

/// <summary>
/// Estensioni di registrazione dei servizi del layer Infrastructure.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra DbContext, contesto tenant, repository ed email service. Lancia se manca la connection string.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration["DATABASE_URL"]
            ?? configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string mancante: impostare DATABASE_URL o ConnectionStrings:Database.");

        // WHY (R-12): EnableRetryOnFailure rende il DbContext resiliente agli errori transitori del DB
        // (riavvii, failover). Le transazioni manuali (BookingService) girano dentro un'execution strategy
        // compatibile con il retry. Npgsql gestisce nativamente i codici di errore transitori.
        services.AddDbContext<BookingSystemDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TimestampInterceptor()));

        // Cache in-memory per dati quasi-statici (risoluzione API key, config/orari/servizi) — R-15/R-22.
        services.AddMemoryCache();
        services.AddSingleton<ITenantCache, TenantCache>();

        // Il tenant corrente vive per-richiesta: scoped, popolato dal middleware, letto dal DbContext.
        services.AddScoped<ITenantContext, TenantContext>();

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPlatformAdminRepository, PlatformAdminRepository>();

        // Admin auth (6.x): generazione/validazione JWT, login.
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAdminAuthService, AdminAuthService>();

        // Platform auth (agency-admin): validazione stamp + login su identità di piattaforma (store separato).
        services.AddScoped<IPlatformSecurityStampService, PlatformSecurityStampService>();
        services.AddScoped<IPlatformAuthService, PlatformAuthService>();
        services.AddScoped<IPlatformAccountService, PlatformAccountService>();

        // Account Owner (onboarding/credenziali): impostazioni, validazione stamp, servizio account.
        services.AddSingleton(AccountSettings.FromConfiguration(configuration));
        services.AddScoped<IUserSecurityStampService, UserSecurityStampService>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();

        // Admin CRUD (6.x): servizi, orari/chiusure, staff, prenotazioni.
        services.AddScoped<IAdminServiceCatalog, AdminServiceCatalog>();
        services.AddScoped<IAdminScheduleManager, AdminScheduleManager>();
        services.AddScoped<IAdminStaffManager, AdminStaffManager>();
        services.AddScoped<IAdminBookingService, AdminBookingService>();
        services.AddScoped<IAdminApiKeyManager, AdminApiKeyManager>();
        services.AddScoped<IGdprDsarService, GdprDsarService>();

        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IBookingService, BookingService>();

        // Provisioning condiviso (CLI + API platform): unica fonte di verità per la creazione tenant.
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();

        // Platform tenant management: crea/lista/dettaglio cross-tenant via API platform.
        services.AddScoped<IPlatformTenantService, PlatformTenantService>();

        // Email (V2 + PH-3): outbox transazionale. L'accodamento (IEmailOutbox) partecipa alla transazione
        // del booking; il dispatcher in background invia col trasporto per-ambiente (AD-10) con retry/backoff.
        AddEmail(services, configuration);

        // Monitor OPS (4.2): scansiona la tabella logs e il DB, recapita alert via canale configurato.
        AddOpsAlerting(services, configuration);

        // Logica cleanup (scoped) + job scheduling (singleton BackgroundService).
        services.AddScoped<IExpiredBookingCleaner, ExpiredBookingCleaner>();
        services.AddHostedService<ExpiredBookingCleanupJob>();

        // Promemoria pre-appuntamento (T2.3): logica scoped + job scheduling.
        services.AddScoped<IReminderEnqueuer, ReminderEnqueuer>();
        services.AddHostedService<ReminderJob>();

        // Retention/erasure GDPR (S2): logica scoped + job scheduling.
        services.AddScoped<IDataRetentionService, DataRetentionService>();
        services.AddHostedService<DataRetentionJob>();

        return services;
    }

    // WHY (PH-3): outbox transazionale. Renderer stateless → singleton. L'accodamento (IEmailOutbox) e il
    // processor sono scoped (usano il DbContext). Il trasporto (IEmailSender) è selezionato una sola volta in
    // base a Email:Provider (AD-10): Mailpit (dev), Brevo (prod), None → no-op. Il dispatcher (singleton hosted)
    // crea uno scope per ciclo e invia le email Pending con retry.
    private static void AddEmail(IServiceCollection services, IConfiguration configuration)
    {
        EmailSettings settings = EmailSettings.FromConfiguration(configuration);
        services.AddSingleton(settings);
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        switch (settings.Provider)
        {
            case EmailProvider.Mailpit:
                services.AddScoped<IEmailSender, MailpitEmailSender>();
                break;

            case EmailProvider.Brevo:
                // HttpClient tipizzato: BaseAddress + header api-key condivisi da tutte le chiamate.
                services.AddHttpClient<BrevoEmailSender>(client =>
                {
                    client.BaseAddress = new Uri("https://api.brevo.com/");
                    client.DefaultRequestHeaders.Add("api-key", settings.BrevoApiKey);
                    client.DefaultRequestHeaders.Add("accept", "application/json");
                });
                services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<BrevoEmailSender>());
                break;

            default:
                services.AddScoped<IEmailSender, NullEmailSender>();
                break;
        }

        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IOutboxEmailProcessor, EmailOutboxProcessor>();

        int pollSeconds = configuration.GetValue<int?>("Email:Outbox:PollSeconds") ?? 30;
        TimeSpan pollInterval = TimeSpan.FromSeconds(Math.Max(pollSeconds, 5));
        services.AddHostedService(sp => new EmailOutboxDispatcher(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<EmailOutboxDispatcher>>(),
            pollInterval));
    }

    // WHY (4.2): monitor OPS. Lo scanner è singleton (mantiene watermark/flag tra i tick); sorgente log e probe DB
    // sono singleton che aprono uno scope per chiamata. Il canale è scelto una sola volta da OpsAlertOptions
    // (Telegram se configurato, altrimenti LogOnly). Se Enabled=false non si registra nulla.
    private static void AddOpsAlerting(IServiceCollection services, IConfiguration configuration)
    {
        OpsAlertOptions options = OpsAlertOptions.FromConfiguration(configuration);
        if (!options.Enabled)
        {
            return;
        }

        // Nome tabella log validato: vive nel progetto Api (DatabaseLogSettings). Lo leggiamo qui dalla config con
        // lo stesso default/whitelist per non creare un riferimento Infrastructure → Api.
        string logTable = configuration["DatabaseLogging:Table"] is { Length: > 0 } t && IsSafeIdentifier(t)
            ? t
            : "logs";

        services.AddSingleton<ILogErrorSource>(sp => new DbLogErrorSource(
            sp.GetRequiredService<IServiceScopeFactory>(), logTable));
        services.AddSingleton<IDbHealthProbe, DbHealthProbe>();

        if (options.Channel == OpsAlertChannelKind.Telegram)
        {
            services.AddHttpClient(TelegramAlertChannel.HttpClientName, client =>
                client.BaseAddress = new Uri($"https://api.telegram.org/bot{options.TelegramBotToken}/"));
            services.AddSingleton<IOpsAlertChannel>(sp => new TelegramAlertChannel(
                sp.GetRequiredService<IHttpClientFactory>(),
                options.TelegramChatId,
                sp.GetRequiredService<ILogger<TelegramAlertChannel>>()));
        }
        else
        {
            services.AddSingleton<IOpsAlertChannel, LogOnlyAlertChannel>();
        }

        services.AddSingleton(sp => new OpsAlertScanner(
            sp.GetRequiredService<ILogErrorSource>(),
            sp.GetRequiredService<IDbHealthProbe>(),
            sp.GetRequiredService<IOpsAlertChannel>(),
            options.Levels,
            DateTimeOffset.UtcNow));

        TimeSpan interval = TimeSpan.FromSeconds(options.PollSeconds);
        services.AddHostedService(sp => new OpsAlertMonitorJob(
            sp.GetRequiredService<OpsAlertScanner>(),
            sp.GetRequiredService<ILogger<OpsAlertMonitorJob>>(),
            interval,
            options.FellBackToLogOnly));
    }

    // WHY: difesa in profondità sul nome tabella (già whitelist altrove). Identificatore Postgres semplice.
    private static bool IsSafeIdentifier(string value) =>
        value.All(c => char.IsLetterOrDigit(c) || c == '_');
}
