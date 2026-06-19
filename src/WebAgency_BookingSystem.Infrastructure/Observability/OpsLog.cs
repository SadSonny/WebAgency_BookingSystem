// [INTENT]: Costante condivisa del marcatore che prefissa OGNI log Error/Fatal emesso dal sottosistema di
// alerting OPS (canale LogOnly, fallimenti del canale Telegram, catch del monitor). Il monitor usa questo
// marcatore per ESCLUDERE i propri log dal digest degli errori applicativi, evitando un feedback-loop di
// auto-alert (i nostri alert finirebbero contati come errori, rigenerando alert all'infinito).

namespace WebAgency_BookingSystem.Infrastructure.Observability;

internal static class OpsLog
{
    /// <summary>Prefisso/marcatore dei log interni del sottosistema OPS, esclusi dal digest.</summary>
    internal const string SelfMarker = "[OPS-ALERT]";
}
