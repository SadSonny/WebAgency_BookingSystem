// [INTENT]: Entry point dell'API. Configura il DI container (Serilog, Infrastructure, validazione,
// rate limiting, OpenAPI), la pipeline middleware (error handling, tenant resolution, rate limiter) e il
// routing degli endpoint pubblici. L'ordine dei middleware è significativo ed è documentato inline.

using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;
using WebAgency_BookingSystem.Api.Endpoints;
using WebAgency_BookingSystem.Api.Endpoints.Admin;
using WebAgency_BookingSystem.Api.Endpoints.Platform;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Api.Logging;
using WebAgency_BookingSystem.Api.Middleware;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Infrastructure;
using WebAgency_BookingSystem.Infrastructure.Auth;
using WebAgency_BookingSystem.Infrastructure.Cors;
using WebAgency_BookingSystem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ── Porta runtime (Railway / PaaS) ────────────────────────────────────────────
// WHY: Railway inietta la porta via $PORT a runtime. La leggiamo qui per far ascoltare Kestrel sulla porta
// assegnata; senza PORT (sviluppo locale / docker run) resta il default dell'immagine (8080).
string? port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// ── Serilog (4.4) ───────────────────────────────────────────────────────────
// WHY: logging strutturato su Console (Railway cattura stdout) E, in tutti gli ambienti, persistenza su DB
// (sink PostgreSQL, ADDITIVO) per consultare i log via SQL — vedi DatabaseLogSink. La request logging di Serilog
// non logga l'IP del cliente di default → GDPR-safe. Livelli e override letti da appsettings (sezione Serilog).
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "WebAgency_BookingSystem.Api")
    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
    .WriteTo.Console()
    .ConfigureDatabaseSink(DatabaseLogSettings.FromConfiguration(context.Configuration)));

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

// Retention dei log applicativi persistiti su DB (purga oltre DatabaseLogging:RetentionDays, default 90).
builder.Services.AddHostedService<LogRetentionJob>();

// ── Forwarded headers (dietro proxy Railway) ──────────────────────────────────
// WHY: dietro il proxy della piattaforma l'IP/scheme reali del client arrivano negli header X-Forwarded-*.
// Senza questo, RemoteIpAddress sarebbe l'IP del proxy → IP errati in audit/log e nel fallback del rate
// limiter. Svuotiamo KnownNetworks/KnownProxies perché l'app è esposta solo dietro il proxy gestito (trusted).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── CORS (R-06 / PH-1) ──────────────────────────────────────────────────────
// WHY: il widget di prenotazione gira nel browser e chiama l'API cross-origin. Le origini ammesse sono
// DINAMICHE e per-tenant (PH-1): derivano dai siteUrl dei tenant attivi (TenantOriginCatalog, aggiornato in
// background), così onboardare un nuovo sito non richiede modifiche di config. In aggiunta, Cors:AllowedOrigins
// resta una lista statica sempre ammessa (nostri tool/dashboard interni). Nessuna credenziale via cookie
// (auth via header X-Api-Key), quindi non serve AllowCredentials. In sviluppo, se non sono configurate origini
// statiche, si accetta qualsiasi origine per comodità di test.
string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
TenantOriginCatalog originCatalog = builder.Services.AddTenantCorsOrigins(builder.Configuration);
var staticOrigins = new HashSet<string>(allowedOrigins, StringComparer.OrdinalIgnoreCase);
bool devAllowAll = builder.Environment.IsDevelopment() && allowedOrigins.Length == 0;
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicies.Frontend, policy =>
    {
        // WHY: SetIsOriginAllowed è sincrona e gira a ogni richiesta → legge solo da strutture in-memory
        // (set statico O(1) + catalogo tenant lock-free), mai dal DB.
        policy.SetIsOriginAllowed(origin =>
            devAllowAll || staticOrigins.Contains(origin) || originCatalog.IsAllowed(origin));

        policy.WithMethods("GET", "POST", "DELETE", "OPTIONS")
              .WithHeaders("X-Api-Key", "Content-Type");
    });
});

// ── Autenticazione admin (JWT Bearer) ─────────────────────────────────────────
// WHY: gli endpoint /admin sono protetti da JWT (AD-08). Il segreto/issuer/audience sono condivisi con il
// generatore (Infrastructure) tramite JwtSettings, così firma e validazione restano coerenti. Le risposte
// 401/403 sono riscritte nell'envelope d'errore del contratto.
JwtSettings jwtSettings = JwtSettings.FromConfiguration(builder.Configuration);

// WHY (S5): in produzione un segreto JWT debole è un rischio critico. Falliamo fast all'avvio se è ancora il
// placeholder di sviluppo (contiene "change-me"): impedisce un deploy con segreto non sostituito.
if (builder.Environment.IsProduction()
    && jwtSettings.Secret.Contains("change-me", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "JWT_SECRET non configurato per la produzione: è ancora il placeholder di sviluppo. Imposta un segreto reale (≥32 char).");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // WHY: manteniamo i nomi dei claim ORIGINALI del token (sub, security_stamp, tenant_id) invece della
        // mappatura legacy che rinomina "sub" → ClaimTypes.NameIdentifier. Sia la generazione (JwtTokenGenerator)
        // sia la lettura (OnTokenValidated, AdminContextMiddleware, endpoint) usano i nomi brevi: senza questo,
        // FindFirst("sub")/("security_stamp") restituirebbe null e ogni richiesta admin fallirebbe con 401.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudiences = [jwtSettings.Audience, jwtSettings.PlatformAudience],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            // WHY: stesso KeyId usato in generazione (JwtTokenGenerator) → il validatore risolve la chiave per
            // "kid" senza passare dal ConfigurationManager vuoto (che causa IDX10517 su Microsoft.IdentityModel 8.x).
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)) { KeyId = JwtSettings.SigningKeyId },
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = JwtRegisteredClaimNames.Sub,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await HttpErrorWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                    "unauthorized", "Autenticazione richiesta o token non valido.", context.HttpContext.RequestAborted);
            },
            OnForbidden = context => HttpErrorWriter.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                "forbidden", "Accesso non consentito.", context.HttpContext.RequestAborted),
            OnTokenValidated = async context =>
            {
                // WHY (SecurityStamp): invalida i JWT emessi prima di un cambio password. Confronto cache-first.
                string? sub = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                string? stampClaim = context.Principal?.FindFirst(AdminClaims.SecurityStamp)?.Value;
                string? role = context.Principal?.FindFirst(ClaimTypes.Role)?.Value;
                if (!Guid.TryParse(sub, out Guid id) || !Guid.TryParse(stampClaim, out Guid stamp))
                {
                    context.Fail("Token privo dei claim richiesti.");
                    return;
                }

                // WHY: token platform e tenant hanno store di stamp diversi; discriminare sul ruolo è obbligatorio,
                // altrimenti un token valido verrebbe rifiutato cercando l'id nello store sbagliato.
                bool ok = role == AdminClaims.PlatformRole
                    ? await context.HttpContext.RequestServices.GetRequiredService<IPlatformSecurityStampService>().IsCurrentAsync(id, stamp, context.HttpContext.RequestAborted)
                    : await context.HttpContext.RequestServices.GetRequiredService<IUserSecurityStampService>().IsCurrentAsync(id, stamp, context.HttpContext.RequestAborted);
                if (!ok) context.Fail("Sessione non più valida.");
            },
        };
    });
builder.Services.AddAuthorization(options =>
{
    // WHY: la validazione JWT accetta ENTRAMBE le audience (tenant + platform). Le policy le separano in modo
    // ESPLICITO: le rotte /platform richiedono ruolo+audience platform; le rotte /admin richiedono l'audience
    // tenant. Così un token platform è rifiutato (403) sulle rotte /admin per policy, non per effetto collaterale.
    options.AddPolicy(AdminClaims.PlatformPolicy, p => p
        .RequireRole(AdminClaims.PlatformRole)
        .RequireClaim("aud", jwtSettings.PlatformAudience));
    options.AddPolicy(AdminClaims.AdminPolicy, p => p
        .RequireAuthenticatedUser()
        .RequireClaim("aud", jwtSettings.Audience));
});

// ── Validazione (FluentValidation) ────────────────────────────────────────────
// I validator degli endpoint pubblici e admin vivono in questo assembly.
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

// ── Rate limiting (4.2 / R-14) ────────────────────────────────────────────────
int permitPerMinute = builder.Configuration.GetValue<int?>("RATE_LIMIT_PER_MINUTE")
    ?? builder.Configuration.GetValue<int?>("RateLimiting:PermitPerMinute")
    ?? 100;

// WHY (R-14): limite per IP applicato a monte della risoluzione tenant, così anche i tentativi con API key
// mancante/non valida (che verrebbero respinti con 401/403 PRIMA del limiter per-chiave) sono limitati →
// protegge da brute-force/enumerazione delle API key e dal carico DB di ogni tentativo. Tipicamente più alto
// del limite per-chiave per non penalizzare un'origine legittima con più chiavi.
int ipPermitPerMinute = builder.Configuration.GetValue<int?>("RATE_LIMIT_IP_PER_MINUTE")
    ?? builder.Configuration.GetValue<int?>("RateLimiting:IpPermitPerMinute")
    ?? 300;

// WHY (S1): la creazione di prenotazioni è l'azione costosa/sensibile (scrive sul DB, manda email). Con una
// API key pubblica esposta nel frontend, va limitata più strettamente del resto per arginare lo spam.
int bookingPerMinute = builder.Configuration.GetValue<int?>("RATE_LIMIT_BOOKING_PER_MINUTE")
    ?? builder.Configuration.GetValue<int?>("RateLimiting:BookingPerMinute")
    ?? 10;

// WHY: il limite account (login/attivazione/reset/cambio) è stringente anti brute-force, ma deve essere
// configurabile perché in test (WebApplicationFactory) l'IP client è null → tutte le chiamate account
// condividono una sola partizione; un limite basso causerebbe 429 spuri. Alzato via env nel factory.
int accountPerMinute = builder.Configuration.GetValue<int?>("RATE_LIMIT_ACCOUNT_PER_MINUTE")
    ?? builder.Configuration.GetValue<int?>("RateLimiting:AccountPerMinute")
    ?? 10;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Limite globale per IP: vale per ogni richiesta /api/v1 (escluso /health), anche prima dell'auth.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        PathString path = httpContext.Request.Path;
        if (!path.StartsWithSegments("/api/v1") || path.StartsWithSegments("/api/v1/health"))
        {
            return RateLimitPartition.GetNoLimiter("__unlimited__");
        }

        string ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter($"ip:{ip}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = ipPermitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        });
    });

    // WHY: la finestra scorrevole per API key approssima un limite continuo (100/min) meglio della fixed
    // window, evitando burst al confine dei minuti. Fallback su IP quando la chiave non è presente.
    options.AddPolicy(RateLimitingPolicies.PublicApi, httpContext =>
    {
        string partitionKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter($"key:{partitionKey}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        });
    });

    // S1: policy stringente per la creazione prenotazioni, partizionata per API key (fallback IP).
    options.AddPolicy(RateLimitingPolicies.BookingCreation, httpContext =>
    {
        string partitionKey = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter($"booking:{partitionKey}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = bookingPerMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        });
    });

    // Account: login/attivazione/reset/cambio password — partizione per IP, limite stringente anti brute-force.
    options.AddPolicy(RateLimitingPolicies.AccountSecurity, httpContext =>
    {
        string ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetSlidingWindowLimiter($"account:{ip}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = accountPerMinute,
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

// ── Migrazioni automatiche all'avvio (opt-in) ─────────────────────────────────
// WHY: comodo in sviluppo e nei deploy a SINGOLA istanza per non lanciare a mano `dotnet ef database update`.
// Disattivato di default (prod-safe): con più istanze in parallelo due avvii concorrenti potrebbero competere
// sulla stessa migrazione, e applicare schema senza revisione è rischioso. In quei casi preferire uno step di
// migrazione dedicato nella pipeline di deploy. Attivare con DB_AUTO_MIGRATE=true (env) o Database:AutoMigrate.
bool autoMigrate = builder.Configuration.GetValue<bool?>("DB_AUTO_MIGRATE")
    ?? builder.Configuration.GetValue<bool?>("Database:AutoMigrate")
    ?? false;
if (autoMigrate)
{
    using IServiceScope migrationScope = app.Services.CreateScope();
    BookingSystemDbContext db = migrationScope.ServiceProvider.GetRequiredService<BookingSystemDbContext>();
    db.Database.Migrate();
    Log.Information("Migrazioni del database applicate all'avvio (DB_AUTO_MIGRATE attivo).");
}

// ── Pipeline middleware (ordine significativo) ────────────────────────────────
// 0. Forwarded headers: per primo, così tutto il resto vede IP/scheme reali del client (dietro proxy).
app.UseForwardedHeaders();

// 0.5 Correlazione (R-02): espone X-Trace-Id nella response (il client/supporto può comunicarlo) e propaga
//     RequestId a tutti i log della richiesta via LogContext, così ogni riga di log è correlabile.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Trace-Id"] = context.TraceIdentifier;
    using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
    {
        await next();
    }
});

// 1. Error handling: rete di sicurezza più esterna, cattura tutto ciò che sta sotto.
app.UseMiddleware<ErrorHandlingMiddleware>();

// 2. Request logging strutturato.
app.UseSerilogRequestLogging();

// 3. Documentazione API (non /api/v1 → esente da tenant resolution).
// WHY (R-13): in produzione non esponiamo pubblicamente l'intera superficie API. Gating per ambiente:
// la doc resta disponibile in Development/Staging dove è utile, non in Production.
if (!app.Environment.IsProduction())
{
    app.MapOpenApi(); // /openapi/v1.json
    app.MapScalarApiReference(options =>
    {
        options.Title = "BookingSystem API";
        options.Theme = ScalarTheme.Purple;
        options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
    }); // /scalar
}

// WHY: con i forwarded headers lo scheme reale è https (TLS terminato dal proxy), quindi la redirection
// non genera loop. In assenza di proxy resta un no-op se la richiesta è già https.
app.UseHttpsRedirection();

// 4. CORS: prima della tenant resolution, così il preflight OPTIONS (privo di X-Api-Key) viene gestito e
//    short-circuitato qui senza essere bloccato con 401 dal middleware tenant.
app.UseCors(CorsPolicies.Frontend);

// 5. Rate limiter PRIMA della tenant resolution (R-14): il GlobalLimiter per IP deve valere anche per i
//    tentativi con API key mancante/non valida (che verrebbero respinti dal middleware tenant). Le policy
//    per-endpoint (RequireRateLimiting) restano applicate perché l'endpoint è già noto dopo il routing.
app.UseRateLimiter();

// 6. Autenticazione/autorizzazione JWT (rotte admin).
app.UseAuthentication();
app.UseAuthorization();

// 7. Risoluzione tenant: da X-Api-Key per le rotte pubbliche, dal JWT per le rotte admin.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<AdminContextMiddleware>();

// ── Endpoint ──────────────────────────────────────────────────────────────────
app.MapPublicEndpoints();   // 5.1-5.8 (API key)
app.MapAdminEndpoints();    // 6.x (JWT)
app.MapPlatformEndpoints(); // /platform (JWT platform)

app.Run();

// WHY: espone il tipo generato dalle top-level statements ai test di integrazione che usano
// WebApplicationFactory<Program>. Senza questa dichiarazione Program è internal e inaccessibile.
public partial class Program { }
