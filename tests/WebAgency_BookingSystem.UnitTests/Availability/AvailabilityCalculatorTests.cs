// [INTENT]: Unit test del cuore dell'algoritmo di disponibilità (AvailabilityCalculator), step 9.1.
// Copre i casi obbligatori della spec 04 di competenza del calcolatore puro: granularità 15 min, bordi di
// chiusura, pausa pranzo, anticipo minimo / date passate, capienza (parallelSlots e staff), e la semantica
// del buffer (D-10). Le responsabilità di risoluzione finestra/chiusure (HoursResolver) sono testate altrove.
// Test deterministici, senza DB: non richiedono Docker.

using WebAgency_BookingSystem.Core.Availability;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.UnitTests.Availability;

public class AvailabilityCalculatorTests
{
    // Identificativi fissi per i test.
    private static readonly Guid ServiceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StaffA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StaffB = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // Giorno di calcolo (futuro) e "adesso" molto precedente: così l'anticipo minimo non filtra nulla,
    // salvo i test specifici sull'anticipo/passato che impostano un tenantNow dedicato.
    private static readonly DateOnly Day = new(2030, 6, 10);
    private static readonly DateTime FarPast = new(2000, 1, 1, 0, 0, 0);

    // ── Helper di costruzione ─────────────────────────────────────────────────
    private static TimeOnly T(string hhmm) => TimeOnly.ParseExact(hhmm, "HH:mm");

    private static DayWindow Window(string open, string close, string? breakStart = null, string? breakEnd = null) =>
        new(T(open), T(close), breakStart is null ? null : T(breakStart), breakEnd is null ? null : T(breakEnd));

    private static ServiceSlotConfig Service(int duration, int parallelSlots = 1, int buffer = 0,
        BufferPosition position = BufferPosition.After) =>
        new(duration, parallelSlots, buffer, position);

    private static BookingSlot Booking(string start, int duration, Guid? staffId = null) =>
        new(T(start), duration, ServiceId, staffId);

    private static IReadOnlyList<SlotResult> Compute(
        DayWindow window, ServiceSlotConfig service, Guid? staffId = null,
        IReadOnlyList<BookingSlot>? bookings = null, DateTime? now = null, int minAdvanceHours = 0) =>
        AvailabilityCalculator.ComputeDay(Day, window, service, ServiceId, staffId,
            bookings ?? [], now ?? FarPast, minAdvanceHours);

    private static IReadOnlyList<string> Times(IReadOnlyList<SlotResult> slots) =>
        slots.Select(s => s.Time.ToString("HH:mm")).ToList();

    private static bool AvailabilityAt(IReadOnlyList<SlotResult> slots, string time) =>
        slots.Single(s => s.Time == T(time)).Available;

    // ── Generazione slot e granularità ────────────────────────────────────────

    [Fact]
    public void Genera_slot_a_granularita_di_15_minuti()
    {
        IReadOnlyList<SlotResult> slots = Compute(Window("09:00", "10:00"), Service(duration: 30));

        Assert.Equal(["09:00", "09:15", "09:30"], Times(slots));
    }

    [Fact]
    public void Arrotonda_il_primo_slot_al_successivo_multiplo_di_15()
    {
        IReadOnlyList<SlotResult> slots = Compute(Window("09:10", "11:00"), Service(duration: 30));

        Assert.Equal("09:15", Times(slots)[0]);
    }

    [Fact]
    public void Slot_che_termina_esattamente_alla_chiusura_e_incluso()
    {
        // open 17:00, close 19:00, durata 60, buffer 0 → ultimo start 18:00 (fine 19:00, bordo incluso); 18:15 escluso.
        IReadOnlyList<SlotResult> slots = Compute(Window("17:00", "19:00"), Service(duration: 60));

        Assert.Contains("18:00", Times(slots));
        Assert.DoesNotContain("18:15", Times(slots));
        Assert.Equal("18:00", Times(slots)[^1]);
    }

    [Fact]
    public void Finestra_troppo_stretta_per_la_durata_non_genera_slot()
    {
        IReadOnlyList<SlotResult> slots = Compute(Window("09:00", "09:20"), Service(duration: 30));

        Assert.Empty(slots);
    }

    // ── Pausa ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Esclude_slot_che_si_sovrappongono_alla_pausa_e_include_quello_che_termina_alla_pausa()
    {
        // pausa 13:00-14:00, durata 60. 12:00 (fine 13:00) incluso; 12:30 (fine 13:30) escluso;
        // 13:00 (dentro pausa) escluso; 14:00 (fine 15:00) incluso.
        IReadOnlyList<SlotResult> slots = Compute(
            Window("12:00", "15:00", "13:00", "14:00"), Service(duration: 60));

        IReadOnlyList<string> times = Times(slots);
        Assert.Contains("12:00", times);
        Assert.DoesNotContain("12:30", times);
        Assert.DoesNotContain("13:00", times);
        Assert.DoesNotContain("13:30", times);
        Assert.Contains("14:00", times);
    }

    // ── Anticipo minimo / date passate ────────────────────────────────────────

    [Fact]
    public void Rimuove_gli_slot_che_violano_l_anticipo_minimo_nel_giorno_corrente()
    {
        // adesso = giorno stesso 09:00, anticipo 1h → primo slot prenotabile 10:00.
        var now = new DateTime(2030, 6, 10, 9, 0, 0);
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "12:00"), Service(duration: 30), now: now, minAdvanceHours: 1);

        Assert.Equal("10:00", Times(slots)[0]);
        Assert.DoesNotContain("09:45", Times(slots));
    }

    [Fact]
    public void Giorno_nel_passato_non_genera_slot()
    {
        // adesso = giorno successivo a Day → tutti gli slot di Day sono nel passato.
        var now = new DateTime(2030, 6, 11, 8, 0, 0);
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "18:00"), Service(duration: 30), now: now, minAdvanceHours: 0);

        Assert.Empty(slots);
    }

    // ── Capienza: parallelSlots (senza staff) ─────────────────────────────────

    [Fact]
    public void ParallelSlots2_con_una_prenotazione_sovrapposta_lo_slot_e_disponibile()
    {
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "10:00"), Service(duration: 30, parallelSlots: 2),
            bookings: [Booking("09:00", 30)]);

        Assert.True(AvailabilityAt(slots, "09:00"));
    }

    [Fact]
    public void ParallelSlots2_con_due_prenotazioni_sovrapposte_lo_slot_non_e_disponibile()
    {
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "10:00"), Service(duration: 30, parallelSlots: 2),
            bookings: [Booking("09:00", 30), Booking("09:00", 30)]);

        Assert.False(AvailabilityAt(slots, "09:00"));
    }

    [Fact]
    public void ParallelSlots1_con_una_prenotazione_sovrapposta_lo_slot_non_e_disponibile()
    {
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "10:00"), Service(duration: 30, parallelSlots: 1),
            bookings: [Booking("09:00", 30)]);

        Assert.False(AvailabilityAt(slots, "09:00"));
    }

    [Fact]
    public void Prenotazione_non_sovrapposta_non_riduce_la_disponibilita()
    {
        // booking 08:00-08:30 non tocca lo slot 09:00-09:30.
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "10:00"), Service(duration: 30, parallelSlots: 1),
            bookings: [Booking("08:00", 30)]);

        Assert.True(AvailabilityAt(slots, "09:00"));
    }

    // ── Capienza: staff individuale ───────────────────────────────────────────

    [Fact]
    public void Staff_con_prenotazione_esistente_sullo_slot_non_e_disponibile()
    {
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "10:00"), Service(duration: 30), staffId: StaffA,
            bookings: [Booking("09:00", 30, StaffA)]);

        Assert.False(AvailabilityAt(slots, "09:00"));
    }

    [Fact]
    public void Staff_con_prenotazione_su_altro_staff_stesso_slot_resta_disponibile()
    {
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "10:00"), Service(duration: 30), staffId: StaffA,
            bookings: [Booking("09:00", 30, StaffB)]);

        Assert.True(AvailabilityAt(slots, "09:00"));
    }

    // ── Buffer (D-10) ─────────────────────────────────────────────────────────

    [Fact]
    public void Buffer_after_lo_slot_immediatamente_dopo_una_prenotazione_non_e_disponibile()
    {
        // durata 30, buffer 15 After. Prenotazione 09:00-09:30. Lo slot 09:30 cade nel buffer della
        // prenotazione esistente → non disponibile.
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "12:00"), Service(duration: 30, parallelSlots: 1, buffer: 15, position: BufferPosition.After),
            bookings: [Booking("09:00", 30)]);

        Assert.False(AvailabilityAt(slots, "09:30"));
    }

    [Fact]
    public void Buffer_zero_lo_slot_immediatamente_dopo_una_prenotazione_e_disponibile()
    {
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "12:00"), Service(duration: 30, parallelSlots: 1, buffer: 0),
            bookings: [Booking("09:00", 30)]);

        Assert.True(AvailabilityAt(slots, "09:30"));
    }

    [Fact]
    public void Buffer_after_riduce_l_ultimo_slot_generabile()
    {
        // open 09:00 close 10:00, durata 30, buffer 15 After → ultimo start = 600-30-15=555=09:15.
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "10:00"), Service(duration: 30, buffer: 15, position: BufferPosition.After));

        Assert.Equal(["09:00", "09:15"], Times(slots));
    }

    [Fact]
    public void Buffer_before_sposta_in_avanti_il_primo_slot_generabile()
    {
        // open 09:00, durata 30, buffer 15 Before → primo start tale che start-15 >= 09:00 → 09:15.
        IReadOnlyList<SlotResult> slots = Compute(
            Window("09:00", "11:00"), Service(duration: 30, buffer: 15, position: BufferPosition.Before));

        Assert.Equal("09:15", Times(slots)[0]);
    }

    // ── IsSlotAvailable (verifica singolo slot, usata in fase di prenotazione) ──

    [Fact]
    public void IsSlotAvailable_slot_libero_dentro_orario_e_true()
    {
        bool ok = AvailabilityCalculator.IsSlotAvailable(
            T("09:00"), Window("09:00", "18:00"), Service(duration: 30), ServiceId, staffId: null, []);

        Assert.True(ok);
    }

    [Fact]
    public void IsSlotAvailable_fuori_orario_di_chiusura_e_false()
    {
        // slot 17:45 + 30 = 18:15 > chiusura 18:00.
        bool ok = AvailabilityCalculator.IsSlotAvailable(
            T("17:45"), Window("09:00", "18:00"), Service(duration: 30), ServiceId, staffId: null, []);

        Assert.False(ok);
    }

    [Fact]
    public void IsSlotAvailable_prima_dell_apertura_e_false()
    {
        bool ok = AvailabilityCalculator.IsSlotAvailable(
            T("08:30"), Window("09:00", "18:00"), Service(duration: 30), ServiceId, staffId: null, []);

        Assert.False(ok);
    }

    [Fact]
    public void IsSlotAvailable_durante_la_pausa_e_false()
    {
        bool ok = AvailabilityCalculator.IsSlotAvailable(
            T("13:00"), Window("09:00", "18:00", "13:00", "14:00"), Service(duration: 30), ServiceId, staffId: null, []);

        Assert.False(ok);
    }

    [Fact]
    public void IsSlotAvailable_capienza_esaurita_e_false()
    {
        bool ok = AvailabilityCalculator.IsSlotAvailable(
            T("09:00"), Window("09:00", "18:00"), Service(duration: 30, parallelSlots: 1),
            ServiceId, staffId: null, [Booking("09:00", 30)]);

        Assert.False(ok);
    }

    [Fact]
    public void IsSlotAvailable_staff_libero_anche_se_altro_staff_occupato_e_true()
    {
        bool ok = AvailabilityCalculator.IsSlotAvailable(
            T("09:00"), Window("09:00", "18:00"), Service(duration: 30),
            ServiceId, staffId: StaffA, [Booking("09:00", 30, StaffB)]);

        Assert.True(ok);
    }
}
