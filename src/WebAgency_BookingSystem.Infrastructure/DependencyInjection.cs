// [INTENT]: Punto unico di registrazione DI del layer Infrastructure. Configura il DbContext (Npgsql +
// snake_case), il TenantContext scoped, i repository e l'implementazione email. Chiamato da Program.cs.
// La connection string si legge da DATABASE_URL (priorità) o dalla sezione ConnectionStrings:Database.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Infrastructure.Auth;
using WebAgency_BookingSystem.Infrastructure.Email;
using WebAgency_BookingSystem.Infrastructure.Persistence;
using WebAgency_BookingSystem.Infrastructure.Persistence.Caching;
using WebAgency_BookingSystem.Infrastructure.Persistence.Interceptors;
using WebAgency_BookingSystem.Infrastructure.Persistence.Repositories;
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

        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IBookingService, BookingService>();

        // V1: email no-op (AD-06). In V2 sostituire con l'implementazione Brevo.
        services.AddScoped<IEmailService, EmailServiceStub>();

        return services;
    }
}
