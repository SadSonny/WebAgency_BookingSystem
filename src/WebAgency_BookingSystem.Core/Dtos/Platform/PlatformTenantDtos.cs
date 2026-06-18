// [INTENT]: DTO di lettura tenant per l'API platform (lista/dettaglio).

namespace WebAgency_BookingSystem.Core.Dtos.Platform;

/// <summary>Riepilogo di un tenant per le API platform (lista e dettaglio).</summary>
public sealed record PlatformTenantSummary(
    Guid Id,
    string Slug,
    string Name,
    string SiteUrl,
    string OwnerEmail,
    bool Active,
    string CreatedAt);
