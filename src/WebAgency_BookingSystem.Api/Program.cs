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
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;
using WebAgency_BookingSystem.Api.Endpoints;
using WebAgency_BookingSystem.Api.Endpoints.Admin;
using WebAgency_BookingSystem.Api.Http;
using WebAgency_BookingSystem.Api.Middleware;
using WebAgency_BookingSystem.Infrastructure;
using WebAgency_BookingSystem.Infrastructure.Auth;

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
// WHY: logging strutturato su Console (Railway cattura stdout). La request logging di Serilog non logga
// l'IP del cliente di default → GDPR-safe. Livelli e override letti da appsettings (sezione Serilog).
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "WebAgency_BookingSystem.Api")
    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
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

// ── CORS (R-06) ───────────────────────────────────────────────────────────────
// WHY: il widget di prenotazione gira nel browser e chiama l'API cross-origin. Le origini ammesse si
// configurano in Cors:AllowedOrigins. Nessuna credenziale via cookie (auth via header X-Api-Key), quindi
// non serve AllowCredentials. In sviluppo, se non sono configurate origini, si accetta qualsiasi origine.
// NOTA: per un multi-tenant pieno le origini ideali derivano dal site_url di ciascun tenant (evoluzione futura).
string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicies.Frontend, policy =>
    {
        if (builder.Environment.IsDevelopment() && allowedOrigins.Length == 0)
        {
            policy.SetIsOriginAllowed(_ => true);
        }
        else
        {
            policy.WithOrigins(allowedOrigins);
        }

        policy.WithMethods("GET", "POST", "DELETE", "OPTIONS")
              .WithHeaders("X-Api-Key", "Content-Type");
    });
});

// ── Autenticazione admin (JWT Bearer) ─────────────────────────────────────────
// WHY: gli endpoint /admin sono protetti da JWT (AD-08). Il segreto/issuer/audience sono condivisi con il
// generatore (Infrastructure) tramite JwtSettings, così firma e validazione restano coerenti. Le risposte
// 401/403 sono riscritte nell'envelope d'errore del contratto.
JwtSettings jwtSettings = JwtSettings.FromConfiguration(builder.Configuration);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
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
        };
    });
builder.Services.AddAuthorization();

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

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await HttpErrorWriter.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests,
            "rate_limit_exceeded", "Troppe richieste. Riprova tra qualche istante.", cancellationToken);
    };
});

var app = builder.Build();

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

app.Run();

// WHY: espone il tipo generato dalle top-level statements ai test di integrazione che usano
// WebApplicationFactory<Program>. Senza questa dichiarazione Program è internal e inaccessibile.
public partial class Program { }
