// [INTENT]: Accesso agli utenti admin. La ricerca per email è GLOBALE (un'email = un account = un'attività) e
// avviene PRIMA della risoluzione del tenant corrente (al login), quindi l'implementazione bypassa il global
// query filter. Gestisce anche i token di sicurezza (attivazione/reset) e la SecurityStamp per l'invalidazione JWT.

using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Abstractions.Repositories;

/// <summary>
/// Repository degli utenti admin di tenant.
/// </summary>
public interface IUserRepository
{
    /// <summary>Restituisce l'utente per email (univoca globale), oppure null. Bypassa il global query filter.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Restituisce l'utente tracked per id (pre-auth, bypassa il filtro tenant), o null.</summary>
    Task<User?> GetTrackedByIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Restituisce la SecurityStamp corrente dell'utente, o null se inesistente.</summary>
    Task<Guid?> GetSecurityStampAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Aggiunge un token di sicurezza invalidando quelli ancora attivi dello stesso scopo. NON persiste:
    /// il chiamante chiama SaveChangesAsync (così può accodare l'email nella stessa transazione).</summary>
    Task AddTokenInvalidatingPreviousAsync(UserSecurityToken token, CancellationToken ct = default);

    /// <summary>Restituisce un token valido (non scaduto, non usato) per hash+scopo, tracked, o null.</summary>
    Task<UserSecurityToken?> GetValidTokenAsync(string tokenHash, SecurityTokenPurpose purpose, CancellationToken ct = default);

    /// <summary>Persiste le modifiche tracked dal repository.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Registra un tentativo di login fallito (S3): incrementa il contatore e, raggiunta la soglia, imposta
    /// il blocco per la durata indicata (azzerando il contatore). Persiste subito.
    /// </summary>
    Task RegisterFailedAttemptAsync(Guid userId, int lockoutThreshold, TimeSpan lockoutDuration, CancellationToken ct = default);

    /// <summary>Registra un login riuscito (S3): azzera contatore e blocco, aggiorna LastLoginAt. Persiste subito.</summary>
    Task RegisterSuccessfulLoginAsync(Guid userId, CancellationToken ct = default);
}
