// [INTENT]: DTO dell'area account Owner: impostazione password da token (attivazione/reset), cambio password
// autenticato, richiesta reset. Record immutabili come da convenzioni.

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>Imposta la password tramite token (attivazione o reset).</summary>
public sealed record SetPasswordRequest(string Token, string NewPassword);

/// <summary>Cambio password autenticato (Owner loggato).</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Richiesta di reset password ("password dimenticata").</summary>
public sealed record PasswordResetRequest(string Email);
