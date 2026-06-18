// [INTENT]: Servizio condiviso di provisioning tenant: crea tenant + configurazioni + Owner (senza password) +
// API key + token/email di attivazione, in un'unica transazione. Usato sia dalla CLI sia dall'API platform, così
// la logica di creazione è una sola fonte di verità.

using WebAgency_BookingSystem.Core.Common;
using WebAgency_BookingSystem.Core.Dtos.Provisioning;
using WebAgency_BookingSystem.Core.Provisioning;

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>Crea un tenant completo a partire da un input di provisioning.</summary>
public interface ITenantProvisioningService
{
    /// <summary>Crea il tenant in transazione. Fallisce con Conflict se lo slug esiste già.</summary>
    Task<Result<ProvisioningOutput>> CreateAsync(ProvisioningInput input, CancellationToken ct = default);
}
