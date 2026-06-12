// [INTENT]: Implementazione scoped di ITenantContext: contenitore del tenant corrente, valorizzato una sola
// volta dal TenantResolutionMiddleware all'inizio della richiesta e poi letto dal DbContext (query filter) e
// dai servizi (regole del tenant). Mutabile-una-volta per impedire cambi di tenant a metà richiesta.

using WebAgency_BookingSystem.Core.Abstractions;
using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Tenancy;

/// <inheritdoc cref="ITenantContext"/>
public sealed class TenantContext : ITenantContext
{
    /// <inheritdoc />
    public Tenant? Tenant { get; private set; }

    /// <inheritdoc />
    public Guid? TenantId => Tenant?.Id;

    /// <inheritdoc />
    public bool IsResolved => Tenant is not null;

    /// <inheritdoc />
    public void SetTenant(Tenant tenant)
    {
        // WHY: il tenant è immutabile per la durata della richiesta. Un secondo SetTenant indicherebbe
        // un bug nella pipeline (doppia risoluzione), non un caso atteso: quindi eccezione.
        if (IsResolved)
        {
            throw new InvalidOperationException("Il tenant è già stato risolto per la richiesta corrente.");
        }

        Tenant = tenant;
    }
}
