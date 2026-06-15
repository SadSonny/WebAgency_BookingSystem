// [INTENT]: DTO per la gestione delle API key del tenant via Admin API (S4, rotazione/revoca). La chiave in
// chiaro è restituita SOLO alla creazione; in lista compaiono solo prefisso/metadati (mai la chiave o l'hash).

namespace WebAgency_BookingSystem.Core.Dtos.Admin;

/// <summary>API key del tenant nella vista admin (senza segreto).</summary>
public sealed record ApiKeyResponse(Guid Id, string KeyPrefix, string? Description, bool Active, string CreatedAt);

/// <summary>Corpo per creare una nuova API key (descrizione opzionale).</summary>
public sealed record CreateApiKeyRequest(string? Description);

/// <summary>Esito creazione: la chiave in chiaro è mostrata UNA sola volta.</summary>
public sealed record CreateApiKeyResponse(Guid Id, string ApiKey, string KeyPrefix);
