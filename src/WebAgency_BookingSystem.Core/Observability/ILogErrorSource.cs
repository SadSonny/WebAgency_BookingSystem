// [INTENT]: Astrazione della lettura degli errori applicativi dalla tabella dei log. Tenere il job disaccoppiato
// dal DB rende la logica di rilevamento unit-testabile con una sorgente fake.

namespace WebAgency_BookingSystem.Core.Observability;

/// <summary>Una riga di log applicativo rilevante per gli alert.</summary>
public sealed record LogError(DateTimeOffset Timestamp, string Level, string Message);

/// <summary>Fornisce gli errori applicativi registrati dopo un certo istante.</summary>
public interface ILogErrorSource
{
    /// <summary>Restituisce i log con timestamp &gt; <paramref name="since"/> e livello incluso in
    /// <paramref name="levels"/>, ordinati per timestamp crescente.</summary>
    Task<IReadOnlyList<LogError>> GetSinceAsync(DateTimeOffset since, IReadOnlyList<string> levels, CancellationToken ct = default);
}
