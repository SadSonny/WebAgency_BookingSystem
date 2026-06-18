// [INTENT]: Contratto del repository per PlatformAdmin. Espone le operazioni di lettura/scrittura usate
// dal flusso di autenticazione e gestione account dell'amministratore di piattaforma (agency-admin).
// Cross-tenant per definizione: nessun filtro tenant applicato.

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Core.Abstractions.Repositories;

/// <summary>
/// Accesso al repository dei PlatformAdmin (agency-admin). Operazioni pre-auth e cross-tenant.
/// </summary>
public interface IPlatformAdminRepository
{
    /// <summary>Restituisce il PlatformAdmin con l'email specificata (non tracciato). Null se non trovato.</summary>
    Task<PlatformAdmin?> GetByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Restituisce il PlatformAdmin con l'id specificato (tracciato, per modifiche). Null se non trovato.</summary>
    Task<PlatformAdmin?> GetTrackedByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Restituisce il SecurityStamp corrente dell'admin (per validazione JWT cache-first). Null se non trovato.</summary>
    Task<Guid?> GetSecurityStampAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Registra un tentativo di login fallito. Se il contatore raggiunge <paramref name="threshold"/>,
    /// imposta il lockout per <paramref name="duration"/> e azzera il contatore.
    /// </summary>
    Task RegisterFailedAttemptAsync(Guid id, int threshold, TimeSpan duration, CancellationToken ct = default);

    /// <summary>Azzera FailedAccessCount, rimuove LockoutEnd e aggiorna LastLoginAt al momento corrente.</summary>
    Task RegisterSuccessfulLoginAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Crea il PlatformAdmin se non esiste per <paramref name="email"/>, altrimenti ne reimposta la password.
    /// Restituisce <c>true</c> se creato, <c>false</c> se la password è stata reimpostata.
    /// </summary>
    Task<bool> UpsertPasswordByEmailAsync(string email, string passwordHash, CancellationToken ct = default);

    /// <summary>Salva le modifiche pendenti nel DbContext.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
