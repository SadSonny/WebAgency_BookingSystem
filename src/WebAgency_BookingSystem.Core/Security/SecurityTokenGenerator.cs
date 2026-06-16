// [INTENT]: Genera token di sicurezza opachi (attivazione/reset). Restituisce il valore in chiaro (da inserire
// nel link email, mostrato una sola volta) e l'hash SHA-256 da conservare. Riusa ApiKeyHasher per avere un'unica
// funzione di hash in tutto il progetto.

using System.Security.Cryptography;

namespace WebAgency_BookingSystem.Core.Security;

/// <summary>Esito della generazione: token in chiaro (per il link) e hash da salvare.</summary>
public readonly record struct GeneratedSecurityToken(string Token, string TokenHash);

/// <summary>Genera token di sicurezza casuali (256 bit) e il relativo hash di conservazione.</summary>
public static class SecurityTokenGenerator
{
    /// <summary>Crea un token casuale URL-safe e il suo hash SHA-256. Il token va comunicato una sola volta.</summary>
    public static GeneratedSecurityToken Generate()
    {
        string token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        return new GeneratedSecurityToken(token, ApiKeyHasher.Hash(token));
    }
}
