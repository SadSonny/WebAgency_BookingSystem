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
    public static DateTime Now(string ianaTimezone) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ResolveZone(ianaTimezone)).DateTime;

    /// <summary>Restituisce la data odierna nel timezone del tenant.</summary>
    public static DateOnly Today(string ianaTimezone) => DateOnly.FromDateTime(Now(ianaTimezone));

    /// <summary>
    /// Converte un orario LOCALE del tenant (data + ora) nell'ISTANTE assoluto corrispondente
    /// (<see cref="DateTimeOffset"/>). WHY (PH-5/R-32): confrontare istanti assoluti — non DateTime locali
    /// "naive" — rende le decisioni di anticipo minimo e preavviso di disdetta corrette anche nei due giorni
    /// l'anno del cambio ora legale, dove sommare/sottrarre ore in ora locale sfaserebbe di un'ora.
    /// </summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, string ianaTimezone)
    {
        TimeZoneInfo zone = ResolveZone(ianaTimezone);
        DateTime local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);

        // WHY: nel salto in avanti (primavera) l'ora locale può non esistere; la spostiamo fuori dal "buco"
        // per ottenere un offset valido. Caso rarissimo per gli orari di apertura, ma gestito per robustezza.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo ResolveZone(string ianaTimezone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaTimezone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
