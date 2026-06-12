// [INTENT]: Espone in modo tipizzato il Tenant risolto, memorizzato in HttpContext.Items dal
// TenantResolutionMiddleware. Evita agli endpoint una seconda query per ricaricare il tenant già caricato
// durante la risoluzione dell'API key.

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Api.Http;

/// <summary>
/// Estensioni di accesso al contesto della richiesta corrente.
/// </summary>
internal static class HttpContextExtensions
{
    internal const string TenantItemKey = "CurrentTenant";

    /// <summary>
    /// Restituisce il <see cref="Tenant"/> risolto per la richiesta. Lancia se chiamato su una rotta non
    /// tenant-scoped (dove il middleware non l'ha popolato): indicherebbe un errore di configurazione pipeline.
    /// </summary>
    public static Tenant CurrentTenant(this HttpContext context) =>
        context.Items[TenantItemKey] as Tenant
        ?? throw new InvalidOperationException("Tenant non risolto: la rotta richiede il TenantResolutionMiddleware.");
}
