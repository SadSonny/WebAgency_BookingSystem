// [INTENT]: Categoria semantica di un errore di business. Disaccoppia il dominio dal trasporto HTTP:
// il layer API traduce ogni ErrorType nello status code appropriato (400/401/403/404/409/500),
// così le entità e i servizi Core non conoscono il protocollo HTTP.

namespace WebAgency_BookingSystem.Core.Common;

/// <summary>
/// Classifica un <see cref="Error"/> per consentirne la mappatura a uno status HTTP nel layer API.
/// </summary>
public enum ErrorType
{
    /// <summary>Errore generico non previsto (→ 500).</summary>
    Failure,

    /// <summary>Input non valido secondo le regole di dominio (→ 400).</summary>
    Validation,

    /// <summary>Risorsa inesistente o non visibile per il tenant corrente (→ 404).</summary>
    NotFound,

    /// <summary>Conflitto con lo stato corrente, es. slot già prenotato (→ 409).</summary>
    Conflict,

    /// <summary>Autenticazione mancante o non valida (→ 401).</summary>
    Unauthorized,

    /// <summary>Autenticato ma non autorizzato all'operazione (→ 403).</summary>
    Forbidden,
}
