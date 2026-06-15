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
using WebAgency_BookingSystem.Infrastructure.Auth;
using WebAgency_BookingSystem.Infrastructure.Email;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Persistence.Caching;
using WebAgency_BookingSystem.Infrastructure.Persistence.Interceptors;
using WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;
using WebAgency_BookingSystem.Infrastructure.Services.Admin;
using WebAgency_BookingSystem.Infrastructure.Services;
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

        // Admin auth (6.x): generazione/validazione JWT, login.
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAdminAuthService, AdminAuthService>();

        // Admin CRUD (6.x): servizi, orari/chiusure, staff, prenotazioni.
        services.AddScoped<IAdminServiceCatalog, AdminServiceCatalog>();
        services.AddScoped<IAdminScheduleManager, AdminScheduleManager>();
        services.AddScoped<IAdminStaffManager, AdminStaffManager>();
        services.AddScoped<IAdminBookingService, AdminBookingService>();
        services.AddScoped<IAdminApiKeyManager, AdminApiKeyManager>();

        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IBookingService, BookingService>();

        // Email (V2 + PH-3): outbox transazionale. L'accodamento (IEmailOutbox) partecipa alla transazione
        // del booking; il dispatcher in background invia col trasporto per-ambiente (AD-10) con retry/backoff.
        AddEmail(services, configuration);

        // Logica cleanup (scoped) + job scheduling (singleton BackgroundService).
        services.AddScoped<IExpiredBookingCleaner, ExpiredBookingCleaner>();
        services.AddHostedService<ExpiredBookingCleanupJob>();

        // Promemoria pre-appuntamento (T2.3): logica scoped + job scheduling.
        services.AddScoped<IReminderEnqueuer, ReminderEnqueuer>();
        services.AddHostedService<ReminderJob>();

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
}
