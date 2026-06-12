// [INTENT]: Risposta del health check (GET /api/v1/health). Usata da Railway come liveness probe.

namespace WebAgency_BookingSystem.Core.Dtos.Public;

/// <summary>
/// Esito del health check del backend.
/// </summary>
/// <param name="Status">Stato sintetico, es. <c>ok</c>.</param>
/// <param name="Timestamp">Istante della verifica in formato ISO 8601 UTC.</param>
public sealed record HealthResponse(string Status, string Timestamp);
