// [INTENT]: Verifica che SecurityTokenGenerator produca token opachi e hash deterministico/coerente con ApiKeyHasher.

using WebAgency_BookingSystem.Core.Security;
using Xunit;

namespace WebAgency_BookingSystem.UnitTests.Security;

public class SecurityTokenGeneratorTests
{
    [Fact]
    public void Generate_ProducesDistinctTokens_WithMatchingHash()
    {
        var a = SecurityTokenGenerator.Generate();
        var b = SecurityTokenGenerator.Generate();

        Assert.NotEqual(a.Token, b.Token);
        Assert.NotEqual(a.TokenHash, b.TokenHash);
        Assert.Equal(a.TokenHash, ApiKeyHasher.Hash(a.Token));
        Assert.True(a.Token.Length >= 32);
    }
}
