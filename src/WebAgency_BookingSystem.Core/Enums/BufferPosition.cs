// [INTENT]: Posizione del buffer (tempo cuscinetto) rispetto a un appuntamento, configurato PER SERVIZIO (AD-03).
// Determina dove l'algoritmo di disponibilità aggiunge i minuti di buffer attorno allo slot occupato.
// Persistito come stringa nel DB (VARCHAR(10)).

namespace WebAgency_BookingSystem.Core.Enums;

/// <summary>
/// Indica dove applicare i minuti di buffer di un servizio rispetto alla durata dell'appuntamento.
/// </summary>
public enum BufferPosition
{
    /// <summary>Buffer solo prima dell'appuntamento.</summary>
    Before,

    /// <summary>Buffer solo dopo l'appuntamento.</summary>
    After,

    /// <summary>Buffer sia prima sia dopo l'appuntamento.</summary>
    Both,
}
