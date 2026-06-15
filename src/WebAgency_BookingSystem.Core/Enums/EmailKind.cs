// [INTENT]: Tipo di email transazionale legata al ciclo di vita di una prenotazione. Persistito come stringa
// nella tabella outbox (PH-3) per leggibilità e stabilità rispetto ai valori numerici.

namespace WebAgency_BookingSystem.Core.Enums;

/// <summary>Categoria dell'email transazionale in coda di invio.</summary>
public enum EmailKind
{
    /// <summary>Conferma della prenotazione inviata al cliente.</summary>
    BookingConfirmation,

    /// <summary>Notifica di nuova prenotazione inviata al titolare.</summary>
    OwnerNotification,

    /// <summary>Conferma di avvenuta disdetta inviata al cliente.</summary>
    CancellationConfirmation,

    /// <summary>Promemoria pre-appuntamento inviato al cliente (T2.3).</summary>
    Reminder,
}
