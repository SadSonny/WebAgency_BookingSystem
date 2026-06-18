// [INTENT]: DTO dell'auth di piattaforma. Riusa AdminTokenResponse per la risposta.

namespace WebAgency_BookingSystem.Core.Dtos.Platform;

/// <summary>Login agency-admin: email + password (identità globale di piattaforma).</summary>
public sealed record PlatformLoginRequest(string Email, string Password);

/// <summary>Setup/break-glass: token operatore + email + password.</summary>
public sealed record PlatformSetupRequest(string SetupToken, string Email, string Password);
