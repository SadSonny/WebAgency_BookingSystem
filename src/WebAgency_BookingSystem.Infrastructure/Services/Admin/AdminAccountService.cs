// [INTENT]: Implementazione di IAdminAccountService. Usa UserSecurityToken (hash) per attivazione/reset, BCrypt
// per gli hash password, l'outbox per le email (conferme + invito reset) e rigenera la SecurityStamp a ogni
// mutazione (invalidando i JWT). La risoluzione tenant/business name avviene via repository (pre-auth → no filtro).

using Microsoft.Extensions.Logging;
using WebAgency_BookingSystem.Core.Abstractions.Repositories;
using WebAgency_BookingSystem.Core.Abstractions.Services;
using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;
using WebAgency_BookingSystem.Core.Security;
using WebAgency_BookingSystem.Infrastructure.Auth;
using WebAgency_BookingSystem.Infrastructure.Email;

namespace WebAgency_BookingSystem.Infrastructure.Services.Admin;

internal sealed class AdminAccountService : IAdminAccountService
{
    private readonly IUserRepository _users;
    private readonly ITenantRepository _tenants;
    private readonly IEmailOutbox _outbox;
    private readonly IUserSecurityStampService _stamps;
    private readonly AccountSettings _settings;
    private readonly ILogger<AdminAccountService> _logger;

    public AdminAccountService(
        IUserRepository users, ITenantRepository tenants, IEmailOutbox outbox,
        IUserSecurityStampService stamps, AccountSettings settings, ILogger<AdminAccountService> logger)
    {
        _users = users;
        _tenants = tenants;
        _outbox = outbox;
        _stamps = stamps;
        _settings = settings;
        _logger = logger;
    }

    public Task<Result> ActivateAsync(SetPasswordRequest request, CancellationToken ct = default) =>
        ApplyTokenPasswordAsync(request, SecurityTokenPurpose.Activation, markActivated: true,
            heading: "Account attivato", confirmation: "Il tuo account è stato attivato. Ora puoi accedere.", ct);

    public Task<Result> ResetPasswordAsync(SetPasswordRequest request, CancellationToken ct = default) =>
        ApplyTokenPasswordAsync(request, SecurityTokenPurpose.PasswordReset, markActivated: false,
            heading: "Password reimpostata", confirmation: "La tua password è stata reimpostata.", ct);

    private async Task<Result> ApplyTokenPasswordAsync(
        SetPasswordRequest request, SecurityTokenPurpose purpose, bool markActivated,
        string heading, string confirmation, CancellationToken ct)
    {
        Error invalid = Error.Validation("token_non_valido", "Link non valido o scaduto. Richiedine uno nuovo.");

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return invalid;
        }

        string hash = ApiKeyHasher.Hash(request.Token);
        UserSecurityToken? token = await _users.GetValidTokenAsync(hash, purpose, ct);
        if (token is null)
        {
            return invalid;
        }

        User? user = await _users.GetTrackedByIdAsync(token.UserId, ct);
        if (user is null)
        {
            return invalid;
        }

        // WHY: token e user sono tracked dallo stesso DbContext → una sola SaveChanges committa tutto insieme.
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.SecurityStamp = Guid.NewGuid();
        if (markActivated)
        {
            user.ActivatedAt = DateTimeOffset.UtcNow;
        }

        token.UsedAt = DateTimeOffset.UtcNow;

        Tenant? tenant = await _tenants.GetByIdAsync(user.TenantId, ct);
        _outbox.EnqueueAccountSecurityConfirmation(user.TenantId, tenant?.Name ?? string.Empty, user.Email, heading, confirmation);

        await _users.SaveChangesAsync(ct);
        _stamps.Invalidate(user.Id);

        _logger.LogInformation("Account: {Purpose} completato per utente {UserId}", purpose, user.Id);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        User? user = await _users.GetTrackedByIdAsync(userId, ct);
        if (user is null || user.PasswordHash is null)
        {
            return Error.Unauthorized("unauthorized", "Operazione non consentita.");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Error.Validation("password_corrente_errata", "La password attuale non è corretta.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.SecurityStamp = Guid.NewGuid();

        Tenant? tenant = await _tenants.GetByIdAsync(user.TenantId, ct);
        _outbox.EnqueueAccountSecurityConfirmation(user.TenantId, tenant?.Name ?? string.Empty, user.Email,
            "Password modificata", "La password del tuo account è stata modificata.");

        await _users.SaveChangesAsync(ct);
        _stamps.Invalidate(user.Id);

        _logger.LogInformation("Account: cambio password per utente {UserId}", user.Id);
        return Result.Success();
    }

    public async Task<Result> RequestPasswordResetAsync(PasswordResetRequest request, CancellationToken ct = default)
    {
        // WHY: risposta SEMPRE di successo (neutra) per non rivelare se l'email è registrata.
        User? user = await _users.GetByEmailAsync(request.Email, ct);
        if (user is { Active: true } && user.PasswordHash is not null)
        {
            GeneratedSecurityToken generated = SecurityTokenGenerator.Generate();
            var token = new UserSecurityToken
            {
                Id = Guid.NewGuid(),
                TenantId = user.TenantId,
                UserId = user.Id,
                TokenHash = generated.TokenHash,
                Purpose = SecurityTokenPurpose.PasswordReset,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(_settings.ResetTokenHours),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await _users.AddTokenInvalidatingPreviousAsync(token, ct); // accoda il token, NON salva ancora

            Tenant? tenant = await _tenants.GetByIdAsync(user.TenantId, ct);
            string url = $"{_settings.PublicBaseUrl}/api/v1/admin/account/password/reset?token={generated.Token}";
            _outbox.EnqueuePasswordReset(user.TenantId, tenant?.Name ?? string.Empty, user.Email, url);

            // WHY: un solo SaveChanges committa token + riga outbox insieme (atomicità).
            await _users.SaveChangesAsync(ct);

            _logger.LogInformation("Account: reset password richiesto per utente {UserId}", user.Id);
        }
        else
        {
            _logger.LogInformation("Account: reset password richiesto per email non registrata (risposta neutra)");
        }

        return Result.Success();
    }
}
