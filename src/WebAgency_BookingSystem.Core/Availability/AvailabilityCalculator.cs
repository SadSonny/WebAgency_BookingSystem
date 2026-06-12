// [INTENT]: Cuore puro dell'algoritmo di disponibilità (spec 04). Genera gli slot di un giorno e verifica
// la capacità, senza alcuna dipendenza da DB o timezone: tutti i dati arrivano già risolti dal layer
// servizio. È deterministico e testabile in isolamento (target degli unit test 9.1).
//
// BUFFER (AD-03, deviazione documentata da spec 04, vedi DUBBI D-10): l'intervallo "occupato" di un
// appuntamento è esteso del buffer secondo BufferPosition (Before/After/Both). Lo stesso padding è applicato
// sia allo slot candidato sia alle prenotazioni esistenti, così un buffer > 0 impedisce di prenotare
// immediatamente prima/dopo un appuntamento (caso di test obbligatorio "slot successivo → non disponibile").

using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Availability;

/// <summary>
/// Algoritmo puro di generazione slot e verifica capacità. Granularità fissa a 15 minuti.
/// </summary>
public static class AvailabilityCalculator
{
    private const int SlotGranularityMinutes = 15;

    /// <summary>
    /// Genera gli slot del giorno con la relativa disponibilità. Restituisce lista vuota se non esistono
    /// slot candidati (giorno passato, finestra troppo stretta, tutti rimossi da pausa/anticipo). Gli slot
    /// occupati restano in lista con <c>Available = false</c>.
    /// </summary>
    /// <param name="date">Giorno da calcolare (locale del tenant).</param>
    /// <param name="window">Finestra oraria effettiva del giorno (tenant o staff).</param>
    /// <param name="service">Configurazione di slot del servizio.</param>
    /// <param name="serviceId">Servizio richiesto (per la capacità aggregata).</param>
    /// <param name="staffId">Staff richiesto, oppure null per disponibilità aggregata sui parallelSlots.</param>
    /// <param name="dayBookings">Prenotazioni confermate del giorno (qualsiasi servizio/staff).</param>
    /// <param name="tenantNow">Data/ora corrente nel timezone del tenant (per l'anticipo minimo).</param>
    /// <param name="minAdvanceHours">Anticipo minimo di prenotazione, in ore.</param>
    public static IReadOnlyList<SlotResult> ComputeDay(
        DateOnly date,
        DayWindow window,
        ServiceSlotConfig service,
        Guid serviceId,
        Guid? staffId,
        IReadOnlyList<BookingSlot> dayBookings,
        DateTime tenantNow,
        int minAdvanceHours)
    {
        int padBefore = PadBefore(service);
        int padAfter = PadAfter(service);

        int openMin = ToMinutes(window.Open);
        int closeMin = ToMinutes(window.Close);

        // L'intervallo occupato (con buffer) deve stare dentro la finestra:
        // start - padBefore >= open  AND  start + duration + padAfter <= close.
        int firstStart = RoundUpToGranularity(openMin + padBefore);
        int lastStart = closeMin - service.DurationMinutes - padAfter;

        DateTime earliestBookable = tenantNow.AddHours(minAdvanceHours);

        var results = new List<SlotResult>();
        for (int start = firstStart; start <= lastStart; start += SlotGranularityMinutes)
        {
            int occStart = start - padBefore;
            int occEnd = start + service.DurationMinutes + padAfter;

            // Pausa: lo slot (con buffer) non deve sovrapporsi alla pausa.
            if (OverlapsBreak(occStart, occEnd, window))
            {
                continue;
            }

            // Anticipo minimo / giorni passati: lo slot deve iniziare non prima del primo orario prenotabile.
            DateTime slotMoment = date.ToDateTime(new TimeOnly(start / 60, start % 60));
            if (slotMoment < earliestBookable)
            {
                continue;
            }

            bool available = HasCapacity(occStart, occEnd, service, serviceId, staffId, dayBookings);
            results.Add(new SlotResult(new TimeOnly(start / 60, start % 60), available));
        }

        return results;
    }

    /// <summary>
    /// Verifica se un singolo slot è prenotabile (usato dalla creazione prenotazione dentro la transazione).
    /// Controlla che l'appuntamento (con buffer) stia dentro la finestra, non tocchi la pausa e abbia capacità.
    /// Le regole temporali (anticipo, finestra visibile, chiusure) sono verificate dal chiamante.
    /// </summary>
    public static bool IsSlotAvailable(
        TimeOnly time,
        DayWindow window,
        ServiceSlotConfig service,
        Guid serviceId,
        Guid? staffId,
        IReadOnlyList<BookingSlot> dayBookings)
    {
        int padBefore = PadBefore(service);
        int padAfter = PadAfter(service);

        int occStart = ToMinutes(time) - padBefore;
        int occEnd = ToMinutes(time) + service.DurationMinutes + padAfter;

        if (occStart < ToMinutes(window.Open) || occEnd > ToMinutes(window.Close))
        {
            return false;
        }

        if (OverlapsBreak(occStart, occEnd, window))
        {
            return false;
        }

        return HasCapacity(occStart, occEnd, service, serviceId, staffId, dayBookings);
    }

    // WHY: la capacità dipende dalla presenza dello staff. Con staff, un solo appuntamento sovrapposto basta
    // a bloccare (uno staff è indivisibile). Senza staff, si contano le prenotazioni dello stesso servizio
    // che si sovrappongono e si confronta con i parallelSlots.
    private static bool HasCapacity(
        int occStart, int occEnd, ServiceSlotConfig service, Guid serviceId, Guid? staffId,
        IReadOnlyList<BookingSlot> dayBookings)
    {
        int padBefore = PadBefore(service);
        int padAfter = PadAfter(service);
        int overlapping = 0;

        foreach (BookingSlot b in dayBookings)
        {
            bool relevant = staffId is not null
                ? b.StaffId == staffId
                : b.ServiceId == serviceId;
            if (!relevant)
            {
                continue;
            }

            int bStart = ToMinutes(b.Start) - padBefore;
            int bEnd = ToMinutes(b.Start) + b.DurationMinutes + padAfter;

            if (occStart < bEnd && occEnd > bStart)
            {
                overlapping++;
            }
        }

        return staffId is not null
            ? overlapping == 0
            : overlapping < service.ParallelSlots;
    }

    private static bool OverlapsBreak(int occStart, int occEnd, DayWindow window)
    {
        if (window.BreakStart is null || window.BreakEnd is null)
        {
            return false;
        }

        int breakStart = ToMinutes(window.BreakStart.Value);
        int breakEnd = ToMinutes(window.BreakEnd.Value);
        return occStart < breakEnd && occEnd > breakStart;
    }

    private static int PadBefore(ServiceSlotConfig s) =>
        s.BufferPosition is BufferPosition.Before or BufferPosition.Both ? s.EffectiveBufferMinutes : 0;

    private static int PadAfter(ServiceSlotConfig s) =>
        s.BufferPosition is BufferPosition.After or BufferPosition.Both ? s.EffectiveBufferMinutes : 0;

    private static int ToMinutes(TimeOnly t) => (t.Hour * 60) + t.Minute;

    private static int RoundUpToGranularity(int minutes)
    {
        int remainder = minutes % SlotGranularityMinutes;
        return remainder == 0 ? minutes : minutes + (SlotGranularityMinutes - remainder);
    }
}
