// [INTENT]: Unit test del renderer dei template email (8.2-8.4). Verificano destinatario corretto per ogni
// tipo di email, presenza dei dati chiave nel corpo, gating del destinatario titolare (OwnerEmail assente),
// inclusione condizionale di staff/prezzo e HTML-encoding dei dati provenienti dal cliente (anti-injection).

using WebAgency_BookingSystem.Core.Entities;
using WebAgency_BookingSystem.Infrastructure.Email;

namespace WebAgency_BookingSystem.UnitTests.Email;

public class EmailTemplateRendererTests
{
    private static readonly EmailTemplateRenderer Sut = new();

    private static Booking MakeBooking(
        Staff? staff = null, decimal? price = 25m, string customerName = "Mario Rossi", string ownerEmail = "titolare@salone.it") => new()
    {
        Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        BookingDate = new DateOnly(2035, 1, 15),
        BookingTime = new TimeOnly(10, 30),
        DurationMinutes = 30,
        CustomerName = customerName,
        CustomerPhone = "+39 333 1234567",
        CustomerEmail = "mario@example.it",
        PriceAtBooking = price,
        StaffId = staff?.Id,
        Staff = staff,
        Service = new Service { Name = "Taglio Uomo" },
        Tenant = new Tenant { Name = "Salone Bellezza", OwnerEmail = ownerEmail },
    };

    [Fact]
    public void Confirmation_va_al_cliente_con_oggetto_e_dati_chiave()
    {
        EmailMessage msg = Sut.RenderBookingConfirmation(MakeBooking());

        Assert.Equal("mario@example.it", msg.ToEmail);
        Assert.Equal("Mario Rossi", msg.ToName);
        Assert.Contains("Salone Bellezza", msg.Subject);
        Assert.Contains("Taglio Uomo", msg.HtmlBody);
        Assert.Contains("10:30", msg.HtmlBody);
        Assert.Contains("Mario Rossi", msg.HtmlBody);
        // Il corpo testuale è l'alternativa per i client senza HTML.
        Assert.Contains("Taglio Uomo", msg.TextBody);
    }

    [Fact]
    public void Confirmation_formatta_la_data_in_italiano()
    {
        EmailMessage msg = Sut.RenderBookingConfirmation(MakeBooking());

        // 15 gennaio 2035 → mese in italiano.
        Assert.Contains("gennaio", msg.HtmlBody);
    }

    [Fact]
    public void OwnerNotification_va_al_titolare_e_include_i_dati_cliente()
    {
        EmailMessage msg = Sut.RenderOwnerNotification(MakeBooking());

        Assert.Equal("titolare@salone.it", msg.ToEmail);
        Assert.Contains("+39 333 1234567", msg.HtmlBody);
        Assert.Contains("mario@example.it", msg.HtmlBody);
    }

    [Fact]
    public void OwnerNotification_senza_OwnerEmail_lascia_destinatario_vuoto()
    {
        // WHY: il provider tratta destinatario vuoto come "niente da inviare" (no crash).
        EmailMessage msg = Sut.RenderOwnerNotification(MakeBooking(ownerEmail: ""));

        Assert.Equal(string.Empty, msg.ToEmail);
    }

    [Fact]
    public void Cancellation_va_al_cliente_con_oggetto_di_disdetta()
    {
        EmailMessage msg = Sut.RenderCancellationConfirmation(MakeBooking());

        Assert.Equal("mario@example.it", msg.ToEmail);
        Assert.Contains("disdetta", msg.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Include_lo_staff_solo_se_presente()
    {
        var staff = new Staff { Id = Guid.NewGuid(), Name = "Marco" };

        Assert.Contains("Marco", Sut.RenderBookingConfirmation(MakeBooking(staff: staff)).HtmlBody);
        Assert.DoesNotContain("Operatore", Sut.RenderBookingConfirmation(MakeBooking(staff: null)).HtmlBody);
    }

    [Fact]
    public void Include_il_prezzo_solo_se_presente()
    {
        Assert.Contains("Prezzo", Sut.RenderBookingConfirmation(MakeBooking(price: 25m)).HtmlBody);
        Assert.DoesNotContain("Prezzo", Sut.RenderBookingConfirmation(MakeBooking(price: null)).HtmlBody);
    }

    [Fact]
    public void Encode_dei_dati_cliente_previene_html_injection()
    {
        EmailMessage msg = Sut.RenderBookingConfirmation(MakeBooking(customerName: "<script>alert(1)</script>"));

        Assert.DoesNotContain("<script>", msg.HtmlBody);
        Assert.Contains("&lt;script&gt;", msg.HtmlBody);
    }
}
