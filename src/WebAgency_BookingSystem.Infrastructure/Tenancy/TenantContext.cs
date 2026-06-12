// [INTENT]: Implementazione scoped di ITenantContext: un semplice contenitore del tenant corrente,
// valorizzato una sola volta dal TenantResolutionMiddleware all'inizio della richiesta e poi letto dal
// DbContext per il global query filter. È volutamente mutabile-una-volta per impedire cambi di tenant
// a metà richiesta.

using WebAgency_BookingSystem.Core.Abstractions;

namespace WebAgency_BookingSystem.Infrastructure.Tenancy;

/// <inheritdoc cref="ITenantContext"/>
public sealed class TenantContext : ITenantContext
{
    /// <inheritdoc />
    public Guid? TenantId { get; private set; }

    /// <inheritdoc />
    public bool IsResolved => TenantId.HasValue;

    /// <inheritdoc />
    public void SetTenant(Guid tenantId)
    {
        // WHY: il tenant è immutabile per la durata della richiesta. Un secondo SetTenant indicherebbe
        // un bug nella pipeline (doppia risoluzione), non un caso atteso: quindi eccezione.
        if (IsResolved)
        {
            throw new InvalidOperationException("Il tenant è già stato risolto per la richiesta corrente.");
        }

        TenantId = tenantId;
    }
}
