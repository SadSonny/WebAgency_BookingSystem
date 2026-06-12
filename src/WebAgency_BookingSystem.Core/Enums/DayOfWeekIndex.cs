// [INTENT]: Giorno della settimana come indice persistibile (SMALLINT 0-6) allineato allo schema DB
// (0=Domenica ... 6=Sabato). Coincide volutamente con System.DayOfWeek per conversioni dirette,
// ma è un tipo dedicato per rendere esplicito il contratto di storage negli orari di apertura.

namespace WebAgency_BookingSystem.Core.Enums;

/// <summary>
/// Indice del giorno della settimana usato negli orari (tenant e staff).
/// I valori numerici corrispondono alla colonna <c>day_of_week</c> dello schema DB.
/// </summary>
public enum DayOfWeekIndex : short
{
    Domenica = 0,
    Lunedi = 1,
    Martedi = 2,
    Mercoledi = 3,
    Giovedi = 4,
    Venerdi = 5,
    Sabato = 6,
}
