// [INTENT]: Unit test del validator della richiesta di erase DSAR: email obbligatoria e formalmente valida.

using WebAgency_BookingSystem.Api.Validation;
using WebAgency_BookingSystem.Core.Dtos.Admin;

namespace WebAgency_BookingSystem.UnitTests.Validation;

public class EraseCustomerRequestValidatorTests
{
    private readonly EraseCustomerRequestValidator _sut = new();

    [Fact]
    public void email_valida_passa()
    {
        var result = _sut.Validate(new EraseCustomerRequest("mario@example.it"));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("non-una-email")]
    public void email_mancante_o_invalida_fallisce(string email)
    {
        var result = _sut.Validate(new EraseCustomerRequest(email));
        Assert.False(result.IsValid);
    }
}
