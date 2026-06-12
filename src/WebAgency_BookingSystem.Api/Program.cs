// [INTENT]: Entry point dell'API. Configura il DI container (Serilog, Infrastructure, validazione,
// rate limiting, OpenAPI), la pipeline middleware (error handling, tenant resolution, rate limiter) e il
// routing degli endpoint pubblici. L'ordine dei middleware è significativo ed è documentato inline.

using System.Reflection;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Api.Middleware;
using WebAgency_BookingSystem.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog (4.4) ───────────────────────────────────────────────────────────
// WHY: logging strutturato su Console (Railway cattura stdout). La request logging di Serilog non logga
// l'IP del cliente di default → GDPR-safe. Livelli e override letti da appsettings (sezione Serilog).
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ── OpenAPI (1.0) ─────────────────────────────────────────────────────────────
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

// ── Infrastructure (DbContext, repository, tenant context, email) ─────────────
builder.Services.AddInfrastructure(builder.Configuration);

// ── Validazione (FluentValidation) ────────────────────────────────────────────
// I validator degli endpoint pubblici (es. CreateBookingRequest) vivono in questo assembly.
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

// ── Rate limiting (4.2) ───────────────────────────────────────────────────────
int permitPerMinute = builder.Configuration.GetValue<int?>("RATE_LIMIT_PER_MINUTE")
    ?? builder.Configuration.GetValue<int?>("RateLimiting:PermitPerMinute")
    ?? 100;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // WHY: la finestra scorrevole per API key approssima un limite continuo (100/min) meglio della fixed
    // window, evitando burst al confine dei minuti. Fallback su IP quando la chiave non è presente.
    options.AddPolicy(RateLimitingPolicies.PublicApi, httpContext =>
    {
        string partitionKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await HttpErrorWriter.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests,
            "rate_limit_exceeded", "Troppe richieste. Riprova tra qualche istante.", cancellationToken);
    };
});

var app = builder.Build();

// ── Pipeline middleware (ordine significativo) ────────────────────────────────
// 1. Error handling: rete di sicurezza più esterna, cattura tutto ciò che sta sotto.
app.UseMiddleware<ErrorHandlingMiddleware>();

// 2. Request logging strutturato.
app.UseSerilogRequestLogging();

// 3. Documentazione API (non /api/v1 → esente da tenant resolution).
app.MapOpenApi(); // /openapi/v1.json
app.MapScalarApiReference(options =>
{
    options.Title = "BookingSystem API";
    options.Theme = ScalarTheme.Purple;
    options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
}); // /scalar

app.UseHttpsRedirection();

// 4. Risoluzione tenant da X-Api-Key (popola ITenantContext per le rotte pubbliche tenant-scoped).
app.UseMiddleware<TenantResolutionMiddleware>();

// 5. Rate limiter (applicato agli endpoint che dichiarano RequireRateLimiting).
app.UseRateLimiter();

// ── Endpoint ──────────────────────────────────────────────────────────────────
// TODO step 5 (Blocco E): app.MapPublicEndpoints() — health, config, services, staff, availability, bookings.

app.Run();
