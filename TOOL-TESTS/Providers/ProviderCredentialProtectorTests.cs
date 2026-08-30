using Microsoft.AspNetCore.DataProtection;
using TOOL_SERVER.Providers;

namespace TOOL_TESTS.Providers;

public sealed class ProviderCredentialProtectorTests
{
    [Fact]
    public void Protect_RoundTripsWithoutEmbeddingPlainTextSecret()
    {
        var protector = new ProviderCredentialProtector(new EphemeralDataProtectionProvider());
        const string secret = "secret-provider-key-123456";

        var encrypted = protector.Protect(secret);

        Assert.DoesNotContain(secret, encrypted, StringComparison.Ordinal);
        Assert.Equal(secret, protector.Unprotect(encrypted));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Protect_RejectsEmptySecrets(string secret)
    {
        var protector = new ProviderCredentialProtector(new EphemeralDataProtectionProvider());

        Assert.Throws<ArgumentException>(() => protector.Protect(secret));
    }
}

