// [INTENT]: DTO dell'autenticazione admin. Il login avviene per sola email globale (un'email = un account = un
// tenant): si verifica email+password e si rilascia un JWT. Il tenant è derivato dall'utente, non passato dal client.

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>
/// Richiesta di login admin. L'email è univoca a livello globale e identifica l'account (e quindi il tenant);
/// la password è la credenziale dell'utente admin.
/// </summary>
public sealed record AdminLoginRequest(string Email, string Password);

/// <summary>
/// Risposta al login: token JWT da usare come <c>Authorization: Bearer</c> sugli endpoint admin.
/// </summary>
/// <param name="Token">Il JWT firmato.</param>
/// <param name="TokenType">Sempre <c>Bearer</c>.</param>
/// <param name="ExpiresAt">Scadenza del token in ISO 8601 UTC.</param>
public sealed record AdminTokenResponse(string Token, string TokenType, string ExpiresAt);
