// [INTENT]: Genera una nuova API key tenant (S4). Centralizza il formato (`bk_live_<hex>`), il prefisso
// visibile (per identificarla in lista) e l'hash SHA-256 con cui viene conservata. Usato dall'Admin API di
// rotazione chiavi e (in prospettiva) dalla CLI di provisioning, così il formato resta unico.

using System.Security.Cryptography;

namespace WebAgency_BookingSystem.Core.Security;

/// <summary>Risultato della generazione: chiave in chiaro (da mostrare UNA volta), prefisso e hash da salvare.</summary>
public readonly record struct GeneratedApiKey(string ApiKey, string KeyPrefix, string KeyHash);

/// <summary>
/// Genera API key sicure e il relativo hash di conservazione.
/// </summary>
public static class ApiKeyGenerator
{
    /// <summary>
    /// Crea una nuova chiave casuale (128 bit). La chiave in chiaro va comunicata una sola volta: in DB si
    /// salva solo <see cref="GeneratedApiKey.KeyHash"/> (+ il prefisso non sensibile per riconoscerla).
    /// </summary>
    public static GeneratedApiKey Generate()
    {
        string secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        string apiKey = $"bk_live_{secret}";
        return new GeneratedApiKey(apiKey, secret[..8], ApiKeyHasher.Hash(apiKey));
    }
}
