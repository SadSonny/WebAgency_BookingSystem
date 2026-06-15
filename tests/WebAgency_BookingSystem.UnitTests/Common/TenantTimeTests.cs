// [INTENT]: Unit test di TenantTime.ToInstant (PH-5/R-32): verifica che la conversione orario-locale→istante
// applichi l'offset corretto del fuso del tenant, distinguendo ora solare e ora legale (Europe/Rome), così i
// confronti di anticipo/preavviso sono DST-corretti. Fallback su UTC per timezone non valido.

using WebAgency_BookingSystem.Core.Common;

namespace WebAgency_BookingSystem.UnitTests.Common;

public class TenantTimeTests
{
    private const string Rome = "Europe/Rome";

    [Fact]
    public void ToInstant_estate_applica_offset_legale_CEST()
    {
        // 1 luglio 2025, 10:00 locale a Roma = ora legale (CEST, UTC+2) → 08:00 UTC.
        DateTimeOffset instant = TenantTime.ToInstant(new DateOnly(2025, 7, 1), new TimeOnly(10, 0), Rome);

        Assert.Equal(TimeSpan.FromHours(2), instant.Offset);
        Assert.Equal(new DateTime(2025, 7, 1, 8, 0, 0, DateTimeKind.Utc), instant.UtcDateTime);
    }

    [Fact]
    public void ToInstant_inverno_applica_offset_solare_CET()
    {
        // 1 gennaio 2025, 10:00 locale a Roma = ora solare (CET, UTC+1) → 09:00 UTC.
        DateTimeOffset instant = TenantTime.ToInstant(new DateOnly(2025, 1, 1), new TimeOnly(10, 0), Rome);

        Assert.Equal(TimeSpan.FromHours(1), instant.Offset);
        Assert.Equal(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc), instant.UtcDateTime);
    }

    [Fact]
    public void ToInstant_timezone_non_valido_ripiega_su_utc()
    {
        DateTimeOffset instant = TenantTime.ToInstant(new DateOnly(2025, 7, 1), new TimeOnly(10, 0), "Non/Esiste");

        Assert.Equal(TimeSpan.Zero, instant.Offset);
        Assert.Equal(new DateTime(2025, 7, 1, 10, 0, 0, DateTimeKind.Utc), instant.UtcDateTime);
    }

    [Fact]
    public void ToInstant_due_date_a_cavallo_del_cambio_ora_differiscono_di_offset()
    {
        // Stessa ora locale (10:00) ma una in CET e una in CEST: gli istanti UTC distano 23h, non 24h —
        // ciò che rende sbagliato sommare ore in ora locale "naive" attorno al cambio DST.
        DateTimeOffset winter = TenantTime.ToInstant(new DateOnly(2025, 3, 29), new TimeOnly(10, 0), Rome); // sabato, CET
        DateTimeOffset summer = TenantTime.ToInstant(new DateOnly(2025, 3, 30), new TimeOnly(10, 0), Rome); // domenica, CEST

        Assert.Equal(TimeSpan.FromHours(1), winter.Offset);
        Assert.Equal(TimeSpan.FromHours(2), summer.Offset);
        Assert.Equal(23, (summer.UtcDateTime - winter.UtcDateTime).TotalHours);
    }
}
