// [INTENT]: Contratto per la generazione del contenuto delle email transazionali a partire da una prenotazione.
// Centralizza i template (HTML + testo) in un unico punto riusato da TUTTI i provider (AD-11): così il
// trasporto (Mailpit dev / Brevo prod) resta indipendente dal contenuto e i template hanno parità dev/prod.
// Le proprietà di navigazione della prenotazione (Tenant, Service, eventuale Staff) devono essere già caricate.

using WebAgency_BookingSystem.Core.Entities;

namespace WebAgency_BookingSystem.Infrastructure.Email;

/// <summary>
/// Produce il contenuto renderizzato (<see cref="EmailMessage"/>) delle email legate al ciclo di vita di una
/// prenotazione. Si assume che <see cref="Booking.Tenant"/> e <see cref="Booking.Service"/> siano valorizzati.
/// </summary>
internal interface IEmailTemplateRenderer
{
    /// <summary>Email al cliente con la conferma della prenotazione creata.</summary>
    EmailMessage RenderBookingConfirmation(Booking booking);

    /// <summary>Email al titolare che notifica la nuova prenotazione ricevuta.</summary>
    EmailMessage RenderOwnerNotification(Booking booking);

    /// <summary>Email al cliente con la conferma dell'avvenuta disdetta.</summary>
    EmailMessage RenderCancellationConfirmation(Booking booking);

    /// <summary>Email al cliente con il promemoria dell'appuntamento imminente (T2.3).</summary>
    EmailMessage RenderReminder(Booking booking);

    /// <summary>Email con il link per attivare l'account e impostare la prima password.</summary>
    EmailMessage RenderAccountActivation(string businessName, string toEmail, string activationUrl);

    /// <summary>Email con il link per reimpostare la password.</summary>
    EmailMessage RenderPasswordReset(string businessName, string toEmail, string resetUrl);

    /// <summary>Email di conferma di un'operazione su credenziali (attivazione/cambio/reset).</summary>
    EmailMessage RenderAccountSecurityConfirmation(string businessName, string toEmail, string heading, string message);
}
