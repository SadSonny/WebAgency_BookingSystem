// [INTENT]: Anonimizzazione degli indirizzi IP per conformità GDPR. L'IP del cliente NON viene mai
// persistito o loggato in chiaro: l'ultimo ottetto (IPv4) o gli ultimi gruppi (IPv6) vengono rimossi
// prima di salvarlo nell'audit log o nei log applicativi.

using System.Net;
using System.Net.Sockets;

namespace WebAgency_BookingSystem.Core.Security;

/// <summary>
/// Rimuove la parte identificativa di un indirizzo IP mantenendo solo il prefisso di rete.
/// </summary>
public static class IpAnonymizer
{
    /// <summary>
    /// Restituisce l'IP anonimizzato (IPv4 → ultimo ottetto in <c>xxx</c>, es. <c>192.168.1.xxx</c>;
    /// IPv6 → solo i primi 3 gruppi seguiti da <c>::</c>), oppure null se l'input è vuoto o non valido.
    /// </summary>
    public static string? Anonymize(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out IPAddress? address))
        {
            return null;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            string[] octets = address.ToString().Split('.');
            if (octets.Length == 4)
            {
                octets[3] = "xxx";
                return string.Join('.', octets);
            }
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            string[] groups = address.ToString().Split(':');
            // WHY: manteniamo al più i primi 3 gruppi (48 bit) — sufficiente per diagnostica di rete,
            // insufficiente a identificare il singolo host.
            return string.Join(':', groups.Take(3)) + "::";
        }

        return null;
    }
}
