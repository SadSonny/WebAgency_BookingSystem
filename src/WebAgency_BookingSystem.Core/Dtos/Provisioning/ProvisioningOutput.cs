// [INTENT]: Esito del provisioning: id/slug del tenant, API key generata (da mostrare UNA volta), prefisso e conteggi.

namespace WebAgency_BookingSystem.Core.Dtos.Provisioning;

/// <summary>Risultato della creazione tenant (segreti da mostrare una sola volta).</summary>
public sealed record ProvisioningOutput(
    Guid TenantId, string Slug, string ApiKey, string KeyPrefix, string OwnerEmail,
    int ServiceCount, int StaffCount, int ClosureCount);
