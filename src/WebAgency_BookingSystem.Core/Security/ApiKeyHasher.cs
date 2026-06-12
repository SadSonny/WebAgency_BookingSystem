// [INTENT]: Hashing deterministico delle API key. La stessa funzione è usata dal middleware (per risolvere
// l'header X-Api-Key) e dal provisioning (per salvare l'hash). Deve restare stabile: cambiarla invaliderebbe
// tutte le chiavi esistenti. SHA-256 in esadecimale minuscolo.

using System.Security.Cryptography;
using System.Text;

namespace WebAgency_BookingSystem.Core.Security;

/// <summary>
/// Calcola l'hash SHA-256 di un'API key in chiaro. Nel DB si conserva solo questo hash.
/// </summary>
public static class ApiKeyHasher
{
    /// <summary>
    /// Restituisce l'hash SHA-256 (esadecimale minuscolo) della chiave fornita. Deterministico: la stessa
    /// chiave produce sempre lo stesso hash, permettendo il confronto con il valore memorizzato.
    /// </summary>
    public static string Hash(string apiKey)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexStringLower(digest);
    }
}
