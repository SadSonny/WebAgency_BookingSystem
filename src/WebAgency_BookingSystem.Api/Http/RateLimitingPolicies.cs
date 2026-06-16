// [INTENT]: Nomi delle policy di rate limiting, condivisi tra la configurazione (Program.cs) e gli endpoint
// che le applicano con RequireRateLimiting. Evita stringhe magiche duplicate.

namespace WebAgency_BookingSystem.Api.Http;

/// <summary>
/// Nomi delle policy di rate limiting registrate nell'applicazione.
/// </summary>
internal static class RateLimitingPolicies
{
    /// <summary>Policy per gli endpoint pubblici: sliding window per API key (fallback IP).</summary>
    public const string PublicApi = "PublicApi";

    /// <summary>Policy più stringente per la CREAZIONE di prenotazioni (S1): per API key, limite basso
    /// contro lo spam con chiave pubblica esposta.</summary>
    public const string BookingCreation = "BookingCreation";

    /// <summary>Policy stringente per gli endpoint sensibili dell'account (login, attivazione, reset, cambio
    /// password): partizionata per IP, contro il brute-force delle credenziali.</summary>
    public const string AccountSecurity = "account-security";
}
