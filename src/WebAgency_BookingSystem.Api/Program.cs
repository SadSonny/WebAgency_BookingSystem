// [INTENT]: Entry point dell'API. Configura il DI container, il pipeline middleware e il routing.
// Ogni sezione è strutturata per essere estesa negli step successivi del DEVELOPMENT_PLAN.md.
// I commenti TODO numerati corrispondono agli step del piano — vanno rimossi man mano che vengono implementati.

using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── OpenAPI ───────────────────────────────────────────────────────────────────
// WHY: usiamo il built-in Microsoft.AspNetCore.OpenApi (.NET 10) per la generazione del documento
// e Scalar come UI. Il documento viene generato a runtime dal codice (auto-aggiornato),
// ogni endpoint deve aggiungere metadati espliciti via .WithSummary/.WithDescription/.Produces.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "WebAgency BookingSystem API",
            Version = "v1",
            Description = """
                Backend multi-tenant per la gestione di prenotazioni di attività locali italiane
                (barbershop, estetica, medici, ecc.).

                ## Autenticazione

                | Tipo | Header | Endpoint |
                |---|---|---|
                | API Key pubblica | `X-Api-Key: <chiave>` | Tutti tranne `/health` |
                | JWT admin | `Authorization: Bearer <token>` | `/admin/*` |

                ## Tenant
                Ogni API Key appartiene a un tenant specifico. Tutte le risorse restituite
                sono automaticamente filtrate per il tenant della chiave usata.
                """
        };
        return Task.CompletedTask;
    });
});

// TODO step 3.1: builder.Services.AddDbContext<BookingSystemDbContext>(...)
// TODO step 3.4-3.7: builder.Services.AddScoped<ITenantRepository, TenantRepository>() ecc.
// TODO step 3.8: builder.Services.AddScoped<IEmailService, EmailServiceStub>()
// TODO step 5.5-5.6: builder.Services.AddScoped<IAvailabilityService, AvailabilityService>()
// TODO step 4.2: builder.Services.AddRateLimiter(...)
// TODO step 4.4: Serilog — Log.Logger = new LoggerConfiguration()...

var app = builder.Build();

// ── Documentazione API (Scalar) ───────────────────────────────────────────────
// WHY: abilitato in tutti gli ambienti (non solo Development) per consentire
// l'ispezione dei contratti su staging e Railway. In produzione potrà essere
// condizionato a una variabile d'ambiente se necessario.
app.MapOpenApi(); // documento JSON: /openapi/v1.json
app.MapScalarApiReference(options =>
{
    options.Title = "BookingSystem API";
    options.Theme = ScalarTheme.Purple;
    options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
}); // UI interattiva: /scalar

app.UseHttpsRedirection();

// TODO step 4.1: app.UseMiddleware<TenantResolutionMiddleware>()
// TODO step 4.2: app.UseRateLimiter()
// TODO step 4.3: app.UseMiddleware<ErrorHandlingMiddleware>()

// ── Endpoint ──────────────────────────────────────────────────────────────────
// TODO step 5: app.MapPublicEndpoints() — health, config, services, staff, availability, bookings
// TODO step 6: app.MapAdminEndpoints() — auth JWT, CRUD servizi/staff, gestione prenotazioni

app.Run();
