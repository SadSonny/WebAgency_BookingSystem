// [INTENT]: Modello immutabile di un alert operativo (OPS) prodotto dal monitor interno e recapitato da un
// IOpsAlertChannel. Kind distingue il tipo di evento; Title/Detail sono testo già pronto per la notifica.

namespace WebAgency_BookingSystem.Core.Observability;

/// <summary>Tipo di evento operativo segnalato.</summary>
public enum OpsAlertKind
{
    /// <summary>Riepilogo aggregato di errori applicativi nella finestra di scansione.</summary>
    ErrorDigest,

    /// <summary>Transizione: il database è diventato irraggiungibile.</summary>
    DbDown,

    /// <summary>Transizione: il database è tornato raggiungibile.</summary>
    DbRecovered,
}

/// <summary>Alert operativo pronto per la notifica.</summary>
public sealed record OpsAlert(OpsAlertKind Kind, string Title, string Detail, DateTimeOffset OccurredAt);
