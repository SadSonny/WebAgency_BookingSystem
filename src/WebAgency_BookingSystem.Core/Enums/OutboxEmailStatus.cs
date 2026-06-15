// [INTENT]: Stato di una email nella outbox transazionale (PH-3). Persistito come stringa. Il dispatcher
// processa solo le Pending eleggibili; le Sent sono concluse; le Failed hanno esaurito i tentativi.

namespace WebAgency_BookingSystem.Core.Enums;

/// <summary>Stato di consegna di una email accodata nella outbox.</summary>
public enum OutboxEmailStatus
{
    /// <summary>In attesa di invio (o di un nuovo tentativo dopo un fallimento transitorio).</summary>
    Pending,

    /// <summary>Inviata con successo al provider.</summary>
    Sent,

    /// <summary>Tentativi esauriti: invio definitivamente fallito (richiede intervento/diagnosi).</summary>
    Failed,
}
