// [INTENT]: Verifica che la SecurityStamp portata da un JWT sia ancora quella corrente dell'utente, così un
// cambio password invalida i token precedenti. La lettura è cache-first per non interrogare il DB a ogni
// richiesta admin; Invalidate rimuove la voce di cache dopo una mutazione di password.

namespace WebAgency_BookingSystem.Core.Abstractions.Services;

/// <summary>Convalida/invalida la SecurityStamp degli utenti admin (per l'invalidazione dei JWT).</summary>
public interface IUserSecurityStampService
{
    /// <summary>True se <paramref name="stamp"/> coincide con la stamp corrente dell'utente (cache-first).</summary>
    Task<bool> IsCurrentAsync(Guid userId, Guid stamp, CancellationToken ct = default);

    /// <summary>Rimuove la voce di cache dell'utente: il prossimo controllo rileggerà dal DB.</summary>
    void Invalidate(Guid userId);
}
