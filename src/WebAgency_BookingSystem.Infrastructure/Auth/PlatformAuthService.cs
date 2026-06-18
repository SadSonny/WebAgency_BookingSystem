// [INTENT]: Autenticazione agency-admin di piattaforma. Risolve il PlatformAdmin per email GLOBALE (identità
// separata da User, senza tenant), verifica la password bcrypt e rilascia un JWT di piattaforma (GeneratePlatform).
// In ogni caso di fallimento restituisce lo STESSO errore neutro (401). WHY (D2): duplica deliberatamente la
// struttura di AdminAuthService (lockout/timing) su uno store separato — nessuna astrazione condivisa per design.

using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Auth;

internal sealed class PlatformAuthService : IPlatformAuthService
{
    // S3: dopo questi tentativi falliti consecutivi l'account è bloccato per la durata indicata.
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // WHY: equalizza il tempo di risposta del login. Se l'admin non esiste / non è attivato, verifichiamo la
    // password contro questo hash "fittizio" (stesso work factor di quelli reali) per non far trapelare via timing
    // l'esistenza/attivazione di un'email (oracle di enumerazione). L'hash è generato una sola volta all'avvio.
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword("timing-equalizer-placeholder");

    private readonly IPlatformAdminRepository _admins;
    private readonly IJwtTokenGenerator _jwt;
    private readonly ILogger<PlatformAuthService> _logger;

    public PlatformAuthService(
        IPlatformAdminRepository admins,
        IJwtTokenGenerator jwt,
        ILogger<PlatformAuthService> logger)
    {
        _admins = admins;
        _jwt = jwt;
        _logger = logger;
    }

    public async Task<Result<AdminTokenResponse>> LoginAsync(PlatformLoginRequest request, CancellationToken ct = default)
    {
        Error invalid = Error.Unauthorized("unauthorized", "Credenziali non valide.");

        PlatformAdmin? admin = await _admins.GetByEmailAsync(request.Email, ct);
        if (admin is not { Active: true } || admin.PasswordHash is null)
        {
            // WHY: bruciamo lo stesso tempo di una verifica reale per non rivelare via timing che l'email non
            // esiste o non è attivata (vedi DummyPasswordHash).
            _ = VerifyPassword(request.Password, DummyPasswordHash);
            _logger.LogWarning("Login platform fallito (admin inesistente/non attivo/non attivato)");
            return invalid;
        }

        // S3: account bloccato → respingiamo SENZA verificare la password (e senza rivelare il blocco al client).
        if (admin.LockoutEnd is DateTimeOffset until && until > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Login platform bloccato (lockout attivo) per admin {AdminId}", admin.Id);
            return invalid;
        }

        if (!VerifyPassword(request.Password, admin.PasswordHash))
        {
            await _admins.RegisterFailedAttemptAsync(admin.Id, MaxFailedAttempts, LockoutDuration, ct);
            _logger.LogWarning("Login platform fallito (password errata) per admin {AdminId}", admin.Id);
            return invalid;
        }

        await _admins.RegisterSuccessfulLoginAsync(admin.Id, ct);
        (string token, DateTimeOffset expiresAt) = _jwt.GeneratePlatform(admin.Id, admin.SecurityStamp);
        _logger.LogInformation("Login platform riuscito: admin {AdminId}", admin.Id);

        return Result.Success(new AdminTokenResponse(token, "Bearer", expiresAt.ToString("o")));
    }

    // WHY: un hash malformato in DB farebbe lanciare la verifica bcrypt; lo trattiamo come credenziale non
    // valida (mai un 500), senza rivelare nulla al chiamante. La non-nullità di passwordHash è garantita dal caller.
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
