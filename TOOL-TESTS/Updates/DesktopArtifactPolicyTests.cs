using System.Text.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

public sealed class DesktopArtifactPolicyTests
{
    private static readonly Uri Api = new("https://api.example.test/");

    [Theory]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/1.2-3/app.zip")]
    [InlineData("https://103.179.190.182:8443/models/VideoMaker-app.zip")]
    public void External_source_is_distinct_from_the_authenticated_API(string url) =>
        Assert.Equal(url, DesktopArtifactDownloadPolicy.Validate(Api, url, DesktopArtifactSourceKinds.ExternalVps).AbsoluteUri);

    [Theory]
    [InlineData("https://103.179.190.182/apps/videomaker/app.zip")]
    [InlineData("http://103.179.190.182:8443/apps/videomaker/app.zip")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker-other/app.zip")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/../secret.zip")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/%2e%2e/file.zip")]
    [InlineData("https://user:password@103.179.190.182:8443/apps/videomaker/app.zip")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/app.zip?token=fixture")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/app.zip#fragment")]
    [InlineData("https://103.179.190.182.evil.test:8443/apps/videomaker/app.zip")]
    [InlineData("//103.179.190.182:8443/apps/videomaker/app.zip")]
    [InlineData("https://127.0.0.1/apps/videomaker/app.zip")]
    public void Ambiguous_or_unapproved_external_addresses_are_rejected_without_network(string url) =>
        Assert.Throws<InvalidDataException>(() => DesktopArtifactDownloadPolicy.Validate(Api, url, DesktopArtifactSourceKinds.ExternalVps));

    [Fact]
    public void Internal_routes_cannot_escape_API_origin_even_when_external_host_is_approved()
    {
        Assert.Equal("https://api.example.test/api/download", DesktopArtifactDownloadPolicy.Validate(Api,
            "/api/download", DesktopArtifactSourceKinds.Internal).AbsoluteUri);
        Assert.Throws<InvalidDataException>(() => DesktopArtifactDownloadPolicy.Validate(Api,
            DesktopArtifactDownloadPolicy.ApplicationPrefix + "app.zip", DesktopArtifactSourceKinds.Internal));
        Assert.Throws<InvalidDataException>(() => DesktopArtifactDownloadPolicy.Validate(Api,
            "/api/download", "Unknown"));
        Assert.Throws<InvalidDataException>(() => DesktopArtifactDownloadPolicy.Validate(Api,
            DesktopArtifactDownloadPolicy.ApplicationPrefix + "app.zip", DesktopArtifactSourceKinds.ExternalVps, []));
    }

    [Fact]
    public void Legacy_release_JSON_keeps_legacy_delivery_without_inventing_Setup_metadata()
    {
        const string json = """{"releaseId":"00000000-0000-0000-0000-000000000001","productName":"VideoMaker","version":"1.0.0","buildNumber":1,"channel":"Stable","platform":"win-x64","minimumSupportedVersion":null,"releaseNotes":null,"publishedAtUtc":"2026-10-01T00:00:00Z","fileName":"app.zip","downloadUrl":"/api/download","sizeBytes":1,"sha256":"fixture"}""";
        var result = JsonSerializer.Deserialize<DesktopReleaseResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(DesktopArtifactSourceKinds.Internal, result.SourceKind);
        Assert.Equal(DesktopInstallModes.Legacy, result.InstallMode);
        Assert.Equal(DesktopDistributionProtocol.Legacy, result.DistributionProtocol);
        Assert.Null(result.SetupArtifact);
    }
}
