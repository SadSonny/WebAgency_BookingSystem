// [INTENT]: DTO admin per la gestione delle prenotazioni (step 2.8 per 6.3-6.4). A differenza del pubblico,
// l'admin vede i dati completi del cliente (telefono, note). Il filtro supporta data/staff/servizio/stato.

using WebAgency_BookingSystem.Core.Dtos.Public;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>Filtri per l'elenco prenotazioni admin (tutti opzionali).</summary>
public sealed record AdminBookingFilter(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    Guid? StaffId,
    Guid? ServiceId,
    BookingStatus? Status);

/// <summary>Dati completi del cliente visibili all'admin.</summary>
public sealed record AdminCustomerInfo(string Name, string Phone, string Email, string? Notes);

/// <summary>Rappresentazione completa di una prenotazione per il pannello admin.</summary>
public sealed record AdminBookingResponse(
    Guid Id,
    string Date,
    string Time,
    int DurationMin,
    string Status,
    BookingServiceRef Service,
    BookingStaffRef? Staff,
    AdminCustomerInfo Customer,
    decimal? Price,
    string CreatedAt);

/// <summary>Corpo per aggiornare lo stato di una prenotazione (es. no_show).</summary>
public sealed record UpdateBookingStatusRequest(string Status);
