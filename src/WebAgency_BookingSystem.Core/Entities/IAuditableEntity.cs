// [INTENT]: Marker per le entità con timestamp di creazione/aggiornamento. Permette a un SaveChanges
// interceptor di valorizzare CreatedAt/UpdatedAt in modo centralizzato e coerente (R-27), invece di
// impostarli a mano in ogni punto di creazione/modifica (facile da dimenticare con admin/CLI futuri).

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Entità che traccia istante di creazione e ultimo aggiornamento (UTC).
/// </summary>
public interface IAuditableEntity
{
    /// <summary>Istante di creazione (UTC). Impostato una sola volta alla creazione.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>Istante dell'ultimo aggiornamento (UTC).</summary>
    DateTimeOffset UpdatedAt { get; set; }
}
