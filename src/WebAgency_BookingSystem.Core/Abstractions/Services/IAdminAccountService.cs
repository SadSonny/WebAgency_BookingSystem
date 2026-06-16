// [INTENT]: Operazioni sull'account Owner: attivazione (prima password da token), cambio password autenticato,
// richiesta reset (invio email neutro) e reset (nuova password da token). Tutte rigenerano la SecurityStamp e
// accodano l'email di conferma. I messaggi d'errore sono neutri dove serve (no enumerazione utenti/token).

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>Gestione self-service delle credenziali Owner.</summary>
public interface IAdminAccountService
{
    /// <summary>Attiva l'account impostando la prima password a partire da un token di attivazione valido.</summary>
    Task<Result> ActivateAsync(SetPasswordRequest request, CancellationToken ct = default);

    /// <summary>Cambia la password dell'utente autenticato dopo aver verificato quella corrente.</summary>
    Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);

    /// <summary>Avvia un reset password: se l'email esiste accoda l'email di reset. Esito SEMPRE neutro.</summary>
    Task<Result> RequestPasswordResetAsync(PasswordResetRequest request, CancellationToken ct = default);

    /// <summary>Reimposta la password a partire da un token di reset valido.</summary>
    Task<Result> ResetPasswordAsync(SetPasswordRequest request, CancellationToken ct = default);
}
