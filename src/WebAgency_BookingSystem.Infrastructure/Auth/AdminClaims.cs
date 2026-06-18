// [INTENT]: Nomi dei claim custom del JWT admin, condivisi tra generazione (Infrastructure) e lettura
// (AdminContextMiddleware in Api). user_id usa il claim standard "sub", role usa ClaimTypes.Role.

namespace WebAgency_BookingSystem.Infrastructure.Auth;

/// <summary>
/// Nomi dei claim applicativi presenti nel JWT admin.
/// </summary>
public static class AdminClaims
{
    /// <summary>Claim con l'Id del tenant a cui l'utente admin appartiene.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Claim con la SecurityStamp dell'utente: un cambio password ne invalida i JWT precedenti.</summary>
    public const string SecurityStamp = "security_stamp";

    /// <summary>Valore del claim ruolo per gli admin di piattaforma.</summary>
    public const string PlatformRole = "PlatformAdmin";

    /// <summary>Nome della policy di autorizzazione per le rotte /platform.</summary>
    public const string PlatformPolicy = "PlatformAdmin";

    /// <summary>Nome della policy di autorizzazione per le rotte /admin (richiede l'audience tenant).</summary>
    public const string AdminPolicy = "Admin";
}
