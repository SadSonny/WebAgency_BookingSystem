// [INTENT]: DTO del sottosistema DSAR (GDPR 4.3): export dei dati di un cliente (diritto d'accesso) ed esito
// dell'anonimizzazione/cancellazione (diritto all'oblio). Record immutabili, serializzati come JSON dall'API.

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>Dati personali di un cliente esportati per il diritto d'accesso. Lista eventualmente vuota.</summary>
public sealed record CustomerDataExport(string Email, int Count, IReadOnlyList<BookingExportItem> Bookings);

/// <summary>Una prenotazione del cliente con i suoi dati personali e gli estremi del consenso.</summary>
public sealed record BookingExportItem(
    Guid BookingId,
    string Date,
    string Time,
    int DurationMinutes,
    string CustomerName,
    string CustomerPhone,
    string CustomerEmail,
    string? CustomerNotes,
    bool GdprConsent,
    DateTimeOffset GdprConsentAt,
    string? GdprConsentVersion,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>Richiesta di cancellazione on-demand dei dati di un cliente, identificato per email.</summary>
public sealed record EraseCustomerRequest(string Email);

/// <summary>Esito della cancellazione: quante prenotazioni anonimizzate e quante email outbox eliminate.</summary>
public sealed record ErasureResult(int AnonymizedBookings, int PurgedOutbox);
