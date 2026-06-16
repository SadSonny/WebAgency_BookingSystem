// [INTENT]: Scopo di un token di sicurezza utente: attivazione iniziale dell'account o reset password.
// Persistito come stringa nella tabella user_security_token per leggibilità/stabilità.

namespace WebAgency_BookingSystem.Core.Enums;

/// <summary>Scopo del token di sicurezza utente.</summary>
public enum SecurityTokenPurpose
{
    /// <summary>Attivazione iniziale dell'account (prima impostazione password).</summary>
    Activation,

    /// <summary>Reset della password ("password dimenticata").</summary>
    PasswordReset,
}
