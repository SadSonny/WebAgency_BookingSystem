// [INTENT]: Operazioni sull'account agency-admin: setup/break-glass (crea-o-reimposta per email, gated da env
// token) e cambio password autenticato. Rigenera la SecurityStamp invalidando i JWT precedenti.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Dtos.Platform;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Services.Platform;

internal sealed class PlatformAccountService : IPlatformAccountService
{
    private readonly IPlatformAdminRepository _admins;
    private readonly IPlatformSecurityStampService _stamps;
    private readonly string? _setupToken;
    private readonly ILogger<PlatformAccountService> _logger;

    public PlatformAccountService(IPlatformAdminRepository admins, IPlatformSecurityStampService stamps,
        IConfiguration configuration, ILogger<PlatformAccountService> logger)
    {
        _admins = admins;
        _stamps = stamps;
        _setupToken = configuration["PLATFORM_SETUP_TOKEN"];
        _logger = logger;
    }

    /// <summary>
    /// Crea o reimposta la password dell'agency-admin per email (break-glass). Gated da PLATFORM_SETUP_TOKEN.
    /// Restituisce true se creato, false se reimpostato. 404 se l'env non è configurato; 401 se il token non corrisponde.
    /// </summary>
    public async Task<Result<bool>> SetupAsync(PlatformSetupRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_setupToken))
        {
            // WHY: endpoint disabilitato se l'env non è configurato → 404, non 403, per non rivelare
            // l'esistenza della rotta in ambienti che non la prevedono.
            return Error.NotFound("not_found", "Risorsa non trovata.");
        }

        // WHY: confronto a tempo costante per non rivelare il token, carattere per carattere, via timing.
        // Nota: FixedTimeEquals ritorna false IMMEDIATAMENTE se le lunghezze differiscono — quindi NON è
        // costante sulla lunghezza; accettabile qui perché la lunghezza del token di env non è un segreto.
        byte[] provided = System.Text.Encoding.UTF8.GetBytes(request.SetupToken ?? string.Empty);
        byte[] expected = System.Text.Encoding.UTF8.GetBytes(_setupToken);
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            return Error.Unauthorized("unauthorized", "Token di setup non valido.");
        }

        string hash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        bool created = await _admins.UpsertPasswordByEmailAsync(request.Email, hash, ct);
        _logger.LogInformation("Platform setup: admin {Action} per {Email}",
            created ? "creato" : "reimpostato", request.Email);
        return Result.Success(created);
    }

    /// <summary>
    /// Cambia la password dell'agency-admin autenticato. Verifica la password corrente, aggiorna l'hash
    /// e rigenera SecurityStamp per invalidare i JWT emessi prima della mutazione.
    /// </summary>
    public async Task<Result> ChangePasswordAsync(Guid platformAdminId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        PlatformAdmin? admin = await _admins.GetTrackedByIdAsync(platformAdminId, ct);
        if (admin is null || admin.PasswordHash is null)
            return Error.Unauthorized("unauthorized", "Operazione non consentita.");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, admin.PasswordHash))
            return Error.Validation("password_corrente_errata", "La password attuale non è corretta.");

        admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        // WHY: rigenerare SecurityStamp prima di SaveChanges garantisce che i JWT emessi con lo stamp
        // precedente vengano invalidati non appena la transazione è committata.
        admin.SecurityStamp = Guid.NewGuid();
        await _admins.SaveChangesAsync(ct);
        _stamps.Invalidate(admin.Id);
        return Result.Success();
    }
}
