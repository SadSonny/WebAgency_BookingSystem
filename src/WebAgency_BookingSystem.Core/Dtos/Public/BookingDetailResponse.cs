// [INTENT]: Dettaglio prenotazione (GET /api/v1/bookings/{id}?token=...) per la pagina di gestione del sito.
// `Staff` può essere null (prenotazione senza staff specifico). `CanCancel` e `CancellationDeadline`
// informano il frontend se e fino a quando è possibile disdire. Orari locali del tenant.

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Dettaglio completo di una prenotazione visibile al cliente.
/// </summary>
/// <param name="CanCancel">True se ancora disdicibile (confermata e entro il preavviso minimo).</param>
/// <param name="CancellationDeadline">Termine ultimo per disdire, <c>yyyy-MM-ddTHH:mm:ss</c> locale del tenant.</param>
/// <param name="Service">Servizio principale (compatibilità: primo della sequenza).</param>
/// <param name="Services">Tutti i servizi dell'appuntamento in ordine (T1.3); per il singolo contiene un elemento.</param>
public sealed record BookingDetailResponse(
    Guid BookingId,
    string Status,
    string Date,
    string Time,
    int DurationMin,
    BookingServiceRef Service,
    BookingStaffRef? Staff,
    BookingCustomerRef Customer,
    bool CanCancel,
    string CancellationDeadline,
    IReadOnlyList<BookingServiceRef> Services);

/// <summary>Riferimento sintetico al servizio prenotato.</summary>
public sealed record BookingServiceRef(Guid Id, string Name);

/// <summary>Riferimento sintetico allo staff prenotato.</summary>
public sealed record BookingStaffRef(Guid Id, string Name);

/// <summary>Dati cliente esposti nel dettaglio (nome ed email).</summary>
public sealed record BookingCustomerRef(string Name, string Email);
