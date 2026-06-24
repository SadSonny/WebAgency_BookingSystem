// [INTENT]: Unit test del validator di POST /api/v1/bookings, focalizzati sui vincoli a rischio runtime: il
// limite di lunghezza di GdprConsentVersion (colonna varchar(100) alimentata da un endpoint pubblico) deve
// produrre un 422 pulito invece di un errore DB, e i campi obbligatori/formati restano verificati.

using WebAgency_BookingSystem.Api.Validation;
using WebAgency_BookingSystem.Core.Dtos.Public;

namespace WebAgency_BookingSystem.UnitTests.Validation;

public class CreateBookingRequestValidatorTests
{
    private readonly CreateBookingRequestValidator _sut = new();

    private static CreateBookingRequest Valid(string? consentVersion = null) => new(
        ServiceId: Guid.NewGuid(),
        StaffId: null,
        Date: "2035-01-01",
        Time: "10:00",
        Customer: new CustomerRequest("Mario Rossi", "+39 333 0000000", "mario@example.it", null),
        GdprConsent: true,
        AdditionalServiceIds: null,
        GdprConsentVersion: consentVersion);

    [Fact]
    public void richiesta_valida_passa()
    {
        Assert.True(_sut.Validate(Valid()).IsValid);
    }

    [Fact]
    public void consent_version_assente_passa()
    {
        Assert.True(_sut.Validate(Valid(consentVersion: null)).IsValid);
    }

    [Fact]
    public void consent_version_entro_100_passa()
    {
        Assert.True(_sut.Validate(Valid(consentVersion: new string('a', 100))).IsValid);
    }

    [Fact]
    public void consent_version_oltre_100_fallisce()
    {
        var result = _sut.Validate(Valid(consentVersion: new string('a', 101)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void consenso_non_dato_fallisce()
    {
        var request = Valid() with { GdprConsent = false };
        Assert.False(_sut.Validate(request).IsValid);
    }

    [Fact]
    public void data_malformata_fallisce()
    {
        var request = Valid() with { Date = "01/01/2035" };
        Assert.False(_sut.Validate(request).IsValid);
    }
}
