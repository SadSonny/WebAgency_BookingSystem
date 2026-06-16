// [INTENT]: Impostazioni JWT lette dalla configurazione, condivise tra la GENERAZIONE del token (Infrastructure)
// e la sua VALIDAZIONE (Api), così segreto/issuer/audience restano coerenti. Legge i nomi flat (JWT_SECRET,
// JWT_EXPIRY_HOURS) con priorità, poi la sezione Jwt. Fail-fast se il segreto manca o è troppo corto.

using Microsoft.Extensions.Configuration;

namespace WebAgency_BookingSystem.Infrastructure.Auth;

/// <summary>
/// Parametri di firma e validità dei token JWT admin.
/// </summary>
public sealed record JwtSettings(string Secret, string Issuer, string Audience, int ExpiryHours)
{
    private const int MinSecretLength = 32;

    /// <summary>KeyId ("kid") stabile della chiave di firma simmetrica, condiviso tra generazione e validazione
    /// del JWT. Garantisce che il token porti un header "kid" e che il validatore risolva direttamente la chiave
    /// configurata, evitando IDX10517 ("kid missing") su alcune versioni di Microsoft.IdentityModel.</summary>
    public const string SigningKeyId = "bookingsystem-admin-hs256";

    /// <summary>Costruisce le impostazioni dalla configurazione. Lancia se il segreto manca o è troppo corto.</summary>
    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        string secret = configuration["JWT_SECRET"]
            ?? configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Segreto JWT mancante: impostare JWT_SECRET o Jwt:Secret.");

        if (secret.Length < MinSecretLength)
        {
            throw new InvalidOperationException($"Segreto JWT troppo corto: minimo {MinSecretLength} caratteri.");
        }

        string issuer = configuration["Jwt:Issuer"] ?? "WebAgency_BookingSystem";
        string audience = configuration["Jwt:Audience"] ?? "WebAgency_BookingSystem.Admin";
        int expiryHours = ParseInt(configuration["JWT_EXPIRY_HOURS"])
            ?? ParseInt(configuration["Jwt:ExpiryHours"])
            ?? 8;

        return new JwtSettings(secret, issuer, audience, expiryHours);
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out int parsed) ? parsed : null;
}
