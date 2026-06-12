// [INTENT]: Risolve l'ora corrente nel timezone (IANA) del tenant. Centralizza la conversione UTC→locale
// usata dall'algoritmo di disponibilità, dalle regole di prenotazione/disdetta e dagli endpoint (es. filtro
// chiusure). Con timezone non valido ripiega su UTC per non bloccare il servizio (situazione anomala, da
// correggere nella configurazione del tenant). Solo BCL (System.TimeZoneInfo): resta in Core senza dipendenze.

namespace WebAgency_BookingSystem.Core.Common;

/// <summary>
/// Helper per ottenere data/ora locale del tenant a partire dal suo identificativo timezone IANA.
/// </summary>
public static class TenantTime
{
    /// <summary>Restituisce la data/ora corrente nel timezone del tenant (fallback UTC se id non valido).</summary>
    public static DateTime Now(string ianaTimezone)
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaTimezone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
        }

        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime;
    }

    /// <summary>Restituisce la data odierna nel timezone del tenant.</summary>
    public static DateOnly Today(string ianaTimezone) => DateOnly.FromDateTime(Now(ianaTimezone));
}
