// [INTENT]: Nomi delle policy CORS, condivisi tra configurazione e pipeline. Evita stringhe magiche.

namespace WebAgency_BookingSystem.Api.Http;

/// <summary>
/// Nomi delle policy CORS registrate nell'applicazione.
/// </summary>
internal static class CorsPolicies
{
    /// <summary>Policy per i widget/frontend dei tenant (origini configurate via Cors:AllowedOrigins).</summary>
    public const string Frontend = "Frontend";
}
