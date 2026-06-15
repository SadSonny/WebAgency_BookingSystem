// [INTENT]: Envelope generico per le risposte paginate (P4). Espone gli elementi della pagina corrente più i
// metadati di paginazione, così i client possono iterare senza scaricare interi dataset (es. liste admin di
// prenotazioni di tenant molto attivi).

namespace WebAgency_BookingSystem.Core.Dtos;

/// <summary>
/// Pagina di risultati con metadati. <paramref name="Total"/> è il conteggio complessivo (non solo la pagina).
/// </summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total);
