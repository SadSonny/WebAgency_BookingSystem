// [INTENT]: Unit test di HoursResolver (parte di step 9.1). Copre i casi della spec 04 relativi alla
// risoluzione della finestra oraria del giorno: chiusura straordinaria, giorno chiuso, precedenza degli
// orari staff su quelli tenant, staff senza orari propri (usa tenant), staff non disponibile. Logica pura.

using WebAgency_BookingSystem.Core.Availability;
using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.UnitTests.Availability;

public class HoursResolverTests
{
    private static readonly Guid StaffA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly Day = new(2030, 6, 10);
    private static DayOfWeekIndex Dow => (DayOfWeekIndex)(int)Day.DayOfWeek;

    private static TimeOnly T(string hhmm) => TimeOnly.ParseExact(hhmm, "HH:mm");

    private static IReadOnlyDictionary<DayOfWeekIndex, TenantBusinessHours> TenantHours(
        bool isOpen = true, string? open = "09:00", string? close = "18:00",
        string? breakStart = null, string? breakEnd = null) =>
        new Dictionary<DayOfWeekIndex, TenantBusinessHours>
        {
            [Dow] = new()
            {
                DayOfWeek = Dow,
                IsOpen = isOpen,
                OpenTime = open is null ? null : T(open),
                CloseTime = close is null ? null : T(close),
                BreakStart = breakStart is null ? null : T(breakStart),
                BreakEnd = breakEnd is null ? null : T(breakEnd),
            },
        };

    private static IReadOnlyDictionary<DayOfWeekIndex, StaffBusinessHours> StaffHours(
        bool isAvailable = true, string? start = "10:00", string? end = "16:00",
        string? breakStart = null, string? breakEnd = null) =>
        new Dictionary<DayOfWeekIndex, StaffBusinessHours>
        {
            [Dow] = new()
            {
                StaffId = StaffA,
                DayOfWeek = Dow,
                IsAvailable = isAvailable,
                StartTime = start is null ? null : T(start),
                EndTime = end is null ? null : T(end),
                BreakStart = breakStart is null ? null : T(breakStart),
                BreakEnd = breakEnd is null ? null : T(breakEnd),
            },
        };

    private static readonly IReadOnlyDictionary<DayOfWeekIndex, StaffBusinessHours> NoStaffHours =
        new Dictionary<DayOfWeekIndex, StaffBusinessHours>();

    private static IReadOnlyList<TenantSpecialClosure> Closure(DateOnly from, DateOnly to) =>
        [new() { DateFrom = from, DateTo = to }];

    // ── Chiusure ──────────────────────────────────────────────────────────────

    [Fact]
    public void Chiusura_straordinaria_che_copre_il_giorno_restituisce_null()
    {
        DayWindow? window = HoursResolver.ResolveWindow(
            Day, TenantHours(), NoStaffHours, Closure(Day.AddDays(-1), Day.AddDays(1)), staffId: null);

        Assert.Null(window);
    }

    [Fact]
    public void Chiusura_che_non_copre_il_giorno_non_blocca()
    {
        DayWindow? window = HoursResolver.ResolveWindow(
            Day, TenantHours(), NoStaffHours, Closure(Day.AddDays(5), Day.AddDays(10)), staffId: null);

        Assert.NotNull(window);
    }

    // ── Giorno chiuso / orari mancanti ────────────────────────────────────────

    [Fact]
    public void Giorno_chiuso_settimanalmente_restituisce_null()
    {
        DayWindow? window = HoursResolver.ResolveWindow(
            Day, TenantHours(isOpen: false, open: null, close: null), NoStaffHours, [], staffId: null);

        Assert.Null(window);
    }

    [Fact]
    public void Giorno_assente_dagli_orari_tenant_restituisce_null()
    {
        var empty = new Dictionary<DayOfWeekIndex, TenantBusinessHours>();

        DayWindow? window = HoursResolver.ResolveWindow(Day, empty, NoStaffHours, [], staffId: null);

        Assert.Null(window);
    }

    // ── Orari tenant ──────────────────────────────────────────────────────────

    [Fact]
    public void Senza_staff_usa_gli_orari_del_tenant_inclusa_la_pausa()
    {
        DayWindow? window = HoursResolver.ResolveWindow(
            Day, TenantHours(open: "09:00", close: "18:00", breakStart: "13:00", breakEnd: "14:00"),
            NoStaffHours, [], staffId: null);

        Assert.NotNull(window);
        Assert.Equal(T("09:00"), window!.Open);
        Assert.Equal(T("18:00"), window.Close);
        Assert.Equal(T("13:00"), window.BreakStart);
        Assert.Equal(T("14:00"), window.BreakEnd);
    }

    // ── Precedenza orari staff ────────────────────────────────────────────────

    [Fact]
    public void Staff_senza_orari_propri_usa_gli_orari_del_tenant()
    {
        DayWindow? window = HoursResolver.ResolveWindow(
            Day, TenantHours(open: "09:00", close: "18:00"), NoStaffHours, [], staffId: StaffA);

        Assert.NotNull(window);
        Assert.Equal(T("09:00"), window!.Open);
        Assert.Equal(T("18:00"), window.Close);
    }

    [Fact]
    public void Staff_con_orari_propri_usa_gli_orari_dello_staff()
    {
        DayWindow? window = HoursResolver.ResolveWindow(
            Day, TenantHours(open: "09:00", close: "18:00"),
            StaffHours(start: "10:00", end: "16:00"), [], staffId: StaffA);

        Assert.NotNull(window);
        Assert.Equal(T("10:00"), window!.Open);
        Assert.Equal(T("16:00"), window.Close);
    }

    [Fact]
    public void Staff_non_disponibile_quel_giorno_restituisce_null()
    {
        DayWindow? window = HoursResolver.ResolveWindow(
            Day, TenantHours(), StaffHours(isAvailable: false, start: null, end: null), [], staffId: StaffA);

        Assert.Null(window);
    }

    [Fact]
    public void Orari_staff_ignorati_se_lo_staff_non_e_richiesto()
    {
        // staffId null → anche se esistono orari staff, valgono quelli del tenant.
        DayWindow? window = HoursResolver.ResolveWindow(
            Day, TenantHours(open: "09:00", close: "18:00"),
            StaffHours(start: "10:00", end: "16:00"), [], staffId: null);

        Assert.NotNull(window);
        Assert.Equal(T("09:00"), window!.Open);
        Assert.Equal(T("18:00"), window.Close);
    }
}
