// [INTENT]: Identità di piattaforma (agency-admin): amministra i tenant via API/console. SEPARATA da User (NON ha
// TenantId) per non intaccare l'invariante di tenant-isolation. Password come hash bcrypt (null finché non
// impostata dal setup). SecurityStamp invalida i JWT precedenti al cambio password. Lockout come per User (S3).

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>Amministratore di piattaforma (agenzia), non legato ad alcun tenant.</summary>
public class PlatformAdmin : IAuditableEntity
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }
    /// <summary>Email di login, univoca globale.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Hash bcrypt della password; null finché non impostata.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>Marca di sicurezza: cambia a ogni mutazione di password e invalida i JWT emessi prima.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    /// <summary>Se false, non può autenticarsi.</summary>
    public bool Active { get; set; } = true;
    /// <summary>Tentativi di login falliti consecutivi (S3).</summary>
    public int FailedAccessCount { get; set; }
    /// <summary>Se valorizzato e nel futuro, account bloccato fino a questo istante (UTC).</summary>
    public DateTimeOffset? LockoutEnd { get; set; }
    /// <summary>Istante di attivazione/prima impostazione password (UTC).</summary>
    public DateTimeOffset? ActivatedAt { get; set; }
    /// <summary>Ultimo login riuscito (UTC).</summary>
    public DateTimeOffset? LastLoginAt { get; set; }
    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Istante dell'ultimo aggiornamento (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
