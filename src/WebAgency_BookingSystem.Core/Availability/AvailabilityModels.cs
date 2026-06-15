// [INTENT]: Tipi di input puri per l'algoritmo di disponibilità (AvailabilityCalculator). Sono modelli di
// dominio senza dipendenze da EF/DB: il layer servizio carica i dati dai repository e li traduce in questi
// record, così il calcolo resta puro e testabile in isolamento.

using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Availability;

/// <summary>
/// Configurazione di slot di un servizio. <paramref name="EffectiveBufferMinutes"/> è già 0 se il buffer
/// è disattivato (BufferEnabled = false), così il calcolatore non deve conoscere il flag.
/// </summary>
public sealed record ServiceSlotConfig(
    int DurationMinutes,
    int ParallelSlots,
    int EffectiveBufferMinutes,
    BufferPosition BufferPosition)
{
    /// <summary>Costruisce la configurazione dal <see cref="Service"/>, azzerando il buffer se disattivato.</summary>
    public static ServiceSlotConfig From(Service service) => new(
        service.DurationMinutes,
        service.ParallelSlots,
        service.BufferEnabled ? service.BufferMinutes : 0,
        service.BufferPosition);
}

/// <summary>
/// Finestra oraria effettiva di un giorno (orari del tenant o dello staff già risolti dal layer servizio),
/// con eventuale pausa. Tutti gli orari sono locali del tenant.
/// </summary>
public sealed record DayWindow(
    TimeOnly Open,
    TimeOnly Close,
    TimeOnly? BreakStart,
    TimeOnly? BreakEnd);

/// <summary>
/// Prenotazione confermata esistente, ridotta ai dati necessari al controllo di sovrapposizione.
/// </summary>
public sealed record BookingSlot(
    TimeOnly Start,
    int DurationMinutes,
    Guid ServiceId,
    Guid? StaffId);

/// <summary>
/// Risultato di uno slot calcolato: orario di inizio e disponibilità.
/// </summary>
public sealed record SlotResult(TimeOnly Time, bool Available);

/// <summary>
/// Intervallo orario di indisponibilità "dura" di un operatore (assenza parziale, T1.1): a differenza di una
/// prenotazione non concorre alla capacità, ma rende non prenotabile qualunque slot che vi si sovrapponga.
/// </summary>
public sealed record TimeInterval(TimeOnly Start, TimeOnly End);
