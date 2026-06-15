// [INTENT]: Normalizza un siteUrl di tenant (eventualmente con path, query, maiuscole) nella sua ORIGINE
// canonica `scheme://host[:port]`, formato in cui i browser inviano l'header Origin. Usato da PH-1 per
// confrontare l'Origin di una richiesta CORS con i siti registrati dei tenant. Restituisce null se il
// siteUrl è vuoto o non è un URL assoluto valido.

namespace WebAgency_BookingSystem.Infrastructure.Cors;

/// <summary>
/// Converte un URL di sito nella sua origine canonica, comparabile con l'header <c>Origin</c> del browser.
/// </summary>
internal static class OriginNormalizer
{
    /// <summary>
    /// Estrae l'origine (<c>scheme://host[:port]</c>) da un siteUrl. La porta di default dello schema
    /// (80/443) viene omessa, coerentemente con come i browser compongono l'header Origin. Restituisce
    /// null se l'input non è un URI assoluto http/https.
    /// </summary>
    public static string? FromSiteUrl(string? siteUrl)
    {
        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(siteUrl.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        // WHY: Uri.GetComponents con UriFormat.Unescaped + porta solo se non di default produce esattamente
        // `scheme://host[:port]` in lowercase per scheme/host, che è il formato dell'header Origin del browser.
        string scheme = uri.Scheme;
        string host = uri.Host.ToLowerInvariant();
        return uri.IsDefaultPort
            ? $"{scheme}://{host}"
            : $"{scheme}://{host}:{uri.Port}";
    }
}
