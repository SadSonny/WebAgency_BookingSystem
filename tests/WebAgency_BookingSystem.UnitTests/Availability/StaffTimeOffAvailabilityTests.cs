// [INTENT]: Unit test dell'integrazione assenze-operatore (T1.1) nell'algoritmo puro: le fasce di assenza
// (staffBlocks) rendono non prenotabili gli slot sovrapposti, mentre quelli adiacenti restano disponibili.

using WebAgency_BookingSystem.Core.Availability;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.UnitTests.Availability;

public class StaffTimeOffAvailabilityTests
{
    private static readonly Guid ServiceId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();

    // Finestra 09:00–17:00, servizio 30 min senza buffer, con staff (capacità = 1 per staff).
    private static readonly DayWindow Window = new(new TimeOnly(9, 0), new TimeOnly(17, 0), null, null);
    private static readonly ServiceSlotConfig Config = new(30, 1, 0, BufferPosition.After);

    private static bool Slot(TimeOnly time, params TimeInterval[] blocks) =>
        AvailabilityCalculator.IsSlotAvailable(time, Window, Config, ServiceId, StaffId, [], blocks);

    [Fact]
    public void Slot_dentro_la_fascia_di_assenza_non_disponibile()
    {
        var block = new TimeInterval(new TimeOnly(11, 0), new TimeOnly(12, 0));

        Assert.False(Slot(new TimeOnly(11, 0), block)); // 11:00–11:30 ⊂ assenza
        Assert.False(Slot(new TimeOnly(11, 30), block)); // 11:30–12:00 si sovrappone
    }

    [Fact]
    public void Slot_adiacente_alla_fascia_di_assenza_disponibile()
    {
        var block = new TimeInterval(new TimeOnly(11, 0), new TimeOnly(12, 0));

        Assert.True(Slot(new TimeOnly(10, 30), block)); // 10:30–11:00 tocca ma non si sovrappone
        Assert.True(Slot(new TimeOnly(12, 0), block));  // 12:00–12:30 tocca ma non si sovrappone
    }

    [Fact]
    public void Senza_assenze_slot_disponibile()
    {
        Assert.True(Slot(new TimeOnly(11, 0)));
    }

    [Fact]
    public void ComputeDay_marca_indisponibili_solo_gli_slot_sovrapposti()
    {
        var blocks = new[] { new TimeInterval(new TimeOnly(11, 0), new TimeOnly(12, 0)) };
        DateTime tenantNow = new(2020, 1, 1); // passato → nessun filtro anticipo

        IReadOnlyList<SlotResult> slots = AvailabilityCalculator.ComputeDay(
            new DateOnly(2035, 6, 2), Window, Config, ServiceId, StaffId, [], tenantNow, minAdvanceHours: 1, blocks);

        // Slot interamente prima (10:30) o a partire da 12:00 disponibili; quelli in 11:00–11:45 no.
        Assert.True(slots.Single(s => s.Time == new TimeOnly(10, 30)).Available);
        Assert.False(slots.Single(s => s.Time == new TimeOnly(11, 0)).Available);
        Assert.False(slots.Single(s => s.Time == new TimeOnly(11, 45)).Available);
        Assert.True(slots.Single(s => s.Time == new TimeOnly(12, 0)).Available);
    }
}
