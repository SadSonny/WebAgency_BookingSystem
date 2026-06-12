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
}
