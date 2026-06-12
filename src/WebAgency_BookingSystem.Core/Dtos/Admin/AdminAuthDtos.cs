// [INTENT]: DTO dell'autenticazione admin (step 2.8 parziale per 6.1). Il login identifica il tenant tramite
// lo slug (l'email è unica solo all'interno del tenant), poi verifica email+password e rilascia un JWT.

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>
/// Richiesta di login admin. Lo <paramref name="TenantSlug"/> identifica l'attività; email e password sono
/// le credenziali dell'utente admin di quel tenant.
/// </summary>
public sealed record AdminLoginRequest(string TenantSlug, string Email, string Password);

/// <summary>
/// Risposta al login: token JWT da usare come <c>Authorization: Bearer</c> sugli endpoint admin.
/// </summary>
/// <param name="Token">Il JWT firmato.</param>
/// <param name="TokenType">Sempre <c>Bearer</c>.</param>
/// <param name="ExpiresAt">Scadenza del token in ISO 8601 UTC.</param>
public sealed record AdminTokenResponse(string Token, string TokenType, string ExpiresAt);
