// [INTENT]: Impostazioni dell'area account Owner lette dalla configurazione (sezione Account/env flat).
// Condivise tra API (link nelle email, policy password, scadenze token) e CLI di provisioning (link attivazione).
// PublicBaseUrl è l'URL pubblico del backend usato per costruire i link assoluti di attivazione/reset.

using Microsoft.Extensions.Configuration;

namespace WebAgency_BookingSystem.Infrastructure.Auth;

/// <summary>Parametri dell'onboarding/credenziali Owner.</summary>
public sealed record AccountSettings(
    string PublicBaseUrl,
    int ActivationTokenHours,
    int ResetTokenHours,
    int PasswordMinLength)
{
    /// <summary>Costruisce le impostazioni dalla configurazione con default ragionevoli.</summary>
    public static AccountSettings FromConfiguration(IConfiguration configuration)
    {
        string baseUrl = configuration["PUBLIC_BASE_URL"]
            ?? configuration["Account:PublicBaseUrl"]
            ?? "http://localhost:5022";

        int activation = configuration.GetValue<int?>("Account:ActivationTokenHours") ?? 72;
        int reset = configuration.GetValue<int?>("Account:ResetTokenHours") ?? 1;
        int minLen = configuration.GetValue<int?>("Account:PasswordMinLength") ?? 12;

        return new AccountSettings(baseUrl.TrimEnd('/'), activation, reset, minLen);
    }
}
