// [INTENT]: Riga della OUTBOX email transazionale (PH-3). L'email viene accodata QUI dentro la stessa
// transazione che crea/disdice la prenotazione: se la prenotazione è committata, l'email è garantita in coda
// (niente perdita se il provider è irraggiungibile). Un dispatcher in background la invia con retry/backoff.
// Il contenuto è "congelato" al momento dell'accodamento (già renderizzato), così il dispatcher fa solo trasporto.

using WebAgency_BookingSystem.Core.Enums;

namespace WebAgency_BookingSystem.Core.Entities;

/// <summary>
/// Messaggio email persistito in attesa di invio. Garantisce consegna at-least-once tramite un dispatcher
/// che ritenta gli invii falliti fino a un numero massimo di tentativi.
/// </summary>
public class OutboxEmail : IAuditableEntity
{
    /// <summary>Identificativo univoco (PK).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant proprietario (per scoping e diagnostica).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Prenotazione collegata; null per email non legate a una prenotazione.</summary>
    public Guid? BookingId { get; set; }

    /// <summary>Tipo di email (conferma, notifica titolare, disdetta).</summary>
    public EmailKind Kind { get; set; }

    /// <summary>Stato di consegna.</summary>
    public OutboxEmailStatus Status { get; set; } = OutboxEmailStatus.Pending;

    /// <summary>Indirizzo del destinatario.</summary>
    public string ToEmail { get; set; } = string.Empty;

    /// <summary>Nome visualizzato del destinatario.</summary>
    public string ToName { get; set; } = string.Empty;

    /// <summary>Oggetto dell'email (contenuto congelato all'accodamento).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Corpo HTML (contenuto congelato all'accodamento).</summary>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>Corpo testuale di fallback (contenuto congelato all'accodamento).</summary>
    public string TextBody { get; set; } = string.Empty;

    /// <summary>Numero di tentativi di invio già effettuati.</summary>
    public int Attempts { get; set; }

    /// <summary>Ultimo errore registrato (troncato); null se nessun fallimento.</summary>
    public string? LastError { get; set; }

    /// <summary>Istante a partire dal quale la riga è eleggibile per il prossimo tentativo (backoff).</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>Istante dell'invio andato a buon fine (UTC); null finché non inviata.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Istante di creazione (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Istante dell'ultimo aggiornamento (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    // ── Navigazione ───────────────────────────────────────────────────────────
    public Tenant? Tenant { get; set; }
}
