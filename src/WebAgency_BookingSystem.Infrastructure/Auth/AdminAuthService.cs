// [INTENT]: Autenticazione admin (step 6.1). Risolve il tenant per slug, l'utente per (tenant, email),
// verifica la password bcrypt e rilascia un JWT. In ogni caso di fallimento restituisce lo STESSO errore
// neutro (401) per non rivelare quale parte delle credenziali è errata.

using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Auth;

internal sealed class AdminAuthService : IAdminAuthService
{
    // S3: dopo questi tentativi falliti consecutivi l'account è bloccato per la durata indicata.
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly ITenantRepository _tenants;
    private readonly IUserRepository _users;
    private readonly IJwtTokenGenerator _jwt;
    private readonly ILogger<AdminAuthService> _logger;

    public AdminAuthService(
        ITenantRepository tenants,
        IUserRepository users,
        IJwtTokenGenerator jwt,
        ILogger<AdminAuthService> logger)
    {
        _tenants = tenants;
        _users = users;
        _jwt = jwt;
        _logger = logger;
    }

    public async Task<Result<AdminTokenResponse>> LoginAsync(AdminLoginRequest request, CancellationToken ct = default)
    {
        Error invalid = Error.Unauthorized("unauthorized", "Credenziali non valide.");

        Tenant? tenant = await _tenants.GetBySlugAsync(request.TenantSlug, ct);
        if (tenant is not { Active: true })
        {
            _logger.LogWarning("Login admin fallito: tenant '{Slug}' inesistente o disattivato", request.TenantSlug);
            return invalid;
        }

        User? user = await _users.GetByTenantAndEmailAsync(tenant.Id, request.Email, ct);
        if (user is not { Active: true })
        {
            _logger.LogWarning("Login admin fallito per tenant {TenantId} (utente inesistente/disattivato)", tenant.Id);
            return invalid;
        }

        // S3: account bloccato → respingiamo SENZA verificare la password (e senza rivelare il blocco al client).
        if (user.LockoutEnd is DateTimeOffset until && until > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Login admin bloccato (lockout attivo) per utente {UserId} tenant {TenantId}", user.Id, tenant.Id);
            return invalid;
        }

        if (!VerifyPassword(request.Password, user.PasswordHash))
        {
            await _users.RegisterFailedAttemptAsync(user.Id, MaxFailedAttempts, LockoutDuration, ct);
            _logger.LogWarning("Login admin fallito (password errata) per utente {UserId} tenant {TenantId}", user.Id, tenant.Id);
            return invalid;
        }

        await _users.RegisterSuccessfulLoginAsync(user.Id, ct);
        (string token, DateTimeOffset expiresAt) = _jwt.Generate(user.Id, tenant.Id, user.Role);
        _logger.LogInformation("Login admin riuscito: utente {UserId} tenant {TenantId}", user.Id, tenant.Id);

        return Result.Success(new AdminTokenResponse(token, "Bearer", expiresAt.ToString("o")));
    }

    // WHY: un hash malformato in DB farebbe lanciare la verifica bcrypt; lo trattiamo come credenziale non
    // valida (mai un 500), senza rivelare nulla al chiamante.
    private static bool VerifyPassword(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
