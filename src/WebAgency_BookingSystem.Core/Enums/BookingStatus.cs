// [INTENT]: Stati possibili di una prenotazione. Persistito come stringa snake_case nel DB
// (vedi conversione EF in Infrastructure) per allinearsi ai valori dello schema:
// 'confirmed' | 'cancelled' | 'no_show' | 'completed'.

namespace WebAgency_BookingSystem.Core.Enums;

/// <summary>
/// Ciclo di vita di una prenotazione. Lo stato iniziale alla creazione è <see cref="Confirmed"/>.
/// </summary>
public enum BookingStatus
{
    /// <summary>Prenotazione attiva e valida.</summary>
    Confirmed,

    /// <summary>Disdetta (dal cliente, dal titolare o dal sistema).</summary>
    Cancelled,

    /// <summary>Cliente non presentato all'appuntamento.</summary>
    NoShow,

    /// <summary>Appuntamento svolto e concluso.</summary>
    Completed,
}
