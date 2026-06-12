// [INTENT]: Risolve l'ora corrente nel timezone (IANA) del tenant. Centralizza la conversione UTC→locale
// usata dall'algoritmo di disponibilità e dalle regole di prenotazione/disdetta. Con timezone non valido
// ripiega su UTC per non bloccare il servizio (situazione anomala, da correggere in configurazione tenant).

namespace WebAgency_BookingSystem.Infrastructure.Services;

/// <summary>
/// Helper per ottenere l'ora locale del tenant a partire dal suo identificativo timezone IANA.
/// </summary>
internal static class TenantTime
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
}
