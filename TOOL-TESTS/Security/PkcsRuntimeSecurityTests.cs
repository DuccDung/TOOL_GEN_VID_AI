using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace TOOL_TESTS.Security;

public sealed class PkcsRuntimeSecurityTests
{
    [Fact]
    public void Loaded_net10_asset_contains_both_October_2026_Pkcs_security_fixes()
    {
        // Checking only NuGet's graph is insufficient: an ASP.NET shared-framework assembly
        // with a higher assembly version can replace an app-local 9.x servicing package.
        var assembly = typeof(SignedCms).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(informational));
        var release = informational!.Split('+', 2)[0].Split('-', 2)[0];
        Assert.True(Version.TryParse(release, out var version), "Pkcs must expose a recognizable servicing version.");
        Assert.Equal(10, version!.Major);
        Assert.True(version >= new Version(10, 0, 13),
            $"Loaded Pkcs {release} is below the fixed net10 floor for GHSA-2fmv-rv2c-59x7 and GHSA-846f-f764-76mx.");
    }

    [Fact]
    public void Patched_SignedCms_still_signs_decodes_and_verifies_valid_fixture_data()
    {
        using var key = RSA.Create(2048);
        using var certificate = CreateFixtureCertificate(key);
        var payload = "VideoMaker PKCS signed fixture"u8.ToArray();
        var signed = new SignedCms(new ContentInfo(payload));
        signed.ComputeSignature(new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            IncludeOption = X509IncludeOption.EndCertOnly
        }, silent: true);
        var decoded = new SignedCms();
        decoded.Decode(signed.Encode());
        // The certificate is private to this fixture. Validate the signature, without an OS
        // trust-chain or revocation lookup that would depend on an external service.
        decoded.CheckSignature(verifySignatureOnly: true);
        Assert.Equal(payload, decoded.ContentInfo.Content);
        Assert.Single(decoded.SignerInfos.Cast<SignerInfo>());
    }

    [Fact]
    public void Patched_EnvelopedCms_still_encrypts_decodes_and_decrypts_valid_fixture_data()
    {
        using var key = RSA.Create(2048);
        using var certificate = CreateFixtureCertificate(key);
        var payload = "VideoMaker PKCS encrypted fixture"u8.ToArray();
        var encrypted = new EnvelopedCms(new ContentInfo(payload));
        encrypted.Encrypt(new CmsRecipient(SubjectIdentifierType.IssuerAndSerialNumber, certificate));
        var decoded = new EnvelopedCms();
        decoded.Decode(encrypted.Encode());
        decoded.Decrypt(new X509Certificate2Collection(certificate));
        Assert.Equal(payload, decoded.ContentInfo.Content);
    }

    private static X509Certificate2 CreateFixtureCertificate(RSA key)
    {
        var request = new CertificateRequest("CN=VideoMaker private PKCS fixture", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
    }
}
