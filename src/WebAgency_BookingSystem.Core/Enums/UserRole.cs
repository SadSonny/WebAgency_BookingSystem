// [INTENT]: Ruolo di un utente admin di tenant (AD-02). Predisposizione multi-utente con ruoli.
// Persistito come stringa nel DB (colonna role). V1 usa di fatto solo Owner; Manager è predisposto.

namespace WebAgency_BookingSystem.Core.Enums;

/// <summary>
/// Ruolo di autorizzazione di un utente admin all'interno del proprio tenant.
/// </summary>
public enum UserRole
{
    /// <summary>Titolare dell'attività: accesso completo a tutte le funzioni admin del tenant.</summary>
    Owner,

    /// <summary>Collaboratore con permessi di gestione (predisposizione futura).</summary>
    Manager,
}
