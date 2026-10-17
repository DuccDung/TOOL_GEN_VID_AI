using System.Security.Cryptography;
using System.Text.Json;
using TOOL_MODEL_PUBLISHER;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_TESTS.Updates;

public sealed class ModelRevocationTests
{
    [Fact]
    public async Task RevokeSignsHigherSequenceAndOmitsRevokedArtifactsWithoutEditingTheSource()
    {
        using var fixture = new Fixture();
        var source = File.ReadAllBytes(fixture.Request.CatalogPath);
        var result = await ModelPublisher.RevokeAsync(fixture.Request);
        var bytes = await File.ReadAllBytesAsync(fixture.OutputCatalogPath);
        var verified = ModelCatalogVerifier.Verify(bytes, fixture.Policy, fixture.SourceState, DateTimeOffset.UtcNow);

        Assert.Equal(11, verified.Catalog.Sequence);
        Assert.DoesNotContain(verified.Catalog.Artifacts, artifact => artifact.Sha256 == fixture.QwenHash);
        Assert.Contains(fixture.QwenHash, verified.NextState.RevokedArtifactSha256!);
        Assert.False(result.Published);
        Assert.Equal(source, File.ReadAllBytes(fixture.Request.CatalogPath));
        Assert.False(Directory.Exists(Path.Combine(fixture.Output, "artifacts")));
        Assert.DoesNotContain("PRIVATE", await File.ReadAllTextAsync(Path.Combine(fixture.Output, "public-trust-root.json")));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.CatalogEnvelopeSha256);
    }

    [Fact]
    public async Task RevokeCarriesPriorRevocationsForwardAndCannotReintroduceTheirPayloads()
    {
        using var fixture = new Fixture(existingQwenRevocation: true);
        var piperHash = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.PiperArchiveArtifactId).Sha256;
        await ModelPublisher.RevokeAsync(fixture.Request with { RevokeSha256 = [piperHash] });
        var bytes = await File.ReadAllBytesAsync(fixture.OutputCatalogPath);
        var verified = ModelCatalogVerifier.Verify(bytes, fixture.Policy, fixture.SourceState, DateTimeOffset.UtcNow);
        Assert.Contains(fixture.QwenHash, verified.Catalog.RevokedArtifactSha256!);
        Assert.Contains(piperHash, verified.Catalog.RevokedArtifactSha256!);
        Assert.DoesNotContain(verified.Catalog.Artifacts, artifact => artifact.Sha256 == fixture.QwenHash || artifact.Sha256 == piperHash);
    }

    [Fact]
    public async Task RevokeCanRetireEveryApprovedArtifactWithAnEmptyActiveCatalog()
    {
        using var fixture = new Fixture();
        await ModelPublisher.RevokeAsync(fixture.Request with
        {
            RevokeSha256 = ApprovedModelArtifactPins.All.Select(artifact => artifact.Sha256).ToArray()
        });
        var verified = ModelCatalogVerifier.Verify(await File.ReadAllBytesAsync(fixture.OutputCatalogPath), fixture.Policy,
            fixture.SourceState, DateTimeOffset.UtcNow);
        Assert.Empty(verified.Catalog.Artifacts);
        Assert.Equal(3, verified.Catalog.RevokedArtifactSha256!.Count);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public async Task RevokeCannotRollBackOrReuseTheCurrentSequence(long sequence)
    {
        using var fixture = new Fixture();
        await AssertCode("revocation_sequence_not_increasing", () => ModelPublisher.RevokeAsync(fixture.Request with { Sequence = sequence }));
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task RevokeRejectsUnknownHashesAndNoOpRevocations()
    {
        using var fixture = new Fixture();
        await AssertCode("revocation_hash_not_approved", () => ModelPublisher.RevokeAsync(fixture.Request with
        {
            RevokeSha256 = [new string('0', 64)]
        }));
        Assert.False(Directory.Exists(fixture.Output));

        using var revoked = new Fixture(existingQwenRevocation: true);
        await AssertCode("revocation_no_change", () => ModelPublisher.RevokeAsync(revoked.Request));
        Assert.False(Directory.Exists(revoked.Output));
    }

    [Fact]
    public async Task RevokeRejectsTamperedCatalogEvenIfTheOperatorRecomputesItsEnvelopeHash()
    {
        using var fixture = new Fixture();
        var envelope = JsonSerializer.Deserialize<ModelDistributionEnvelope>(File.ReadAllBytes(fixture.Request.CatalogPath), Fixture.Json)!;
        var payload = Convert.FromBase64String(envelope.PayloadBase64);
        payload[^1] ^= 1;
        var tampered = JsonSerializer.SerializeToUtf8Bytes(envelope with { PayloadBase64 = Convert.ToBase64String(payload) }, Fixture.Json);
        await File.WriteAllBytesAsync(fixture.Request.CatalogPath, tampered);
        await AssertCode("source_catalog_untrusted", () => ModelPublisher.RevokeAsync(fixture.Request with
        {
            CatalogEnvelopeSha256 = Convert.ToHexString(SHA256.HashData(tampered)).ToLowerInvariant()
        }));
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task RevokeRequiresTheExactSourceEnvelopePinAndCurrentTrustedSigningKey()
    {
        using var fixture = new Fixture();
        await AssertCode("catalog_hash_mismatch", () => ModelPublisher.RevokeAsync(fixture.Request with
        {
            CatalogEnvelopeSha256 = new string('0', 64)
        }));
        await AssertCode("signing_key_not_trusted", () => ModelPublisher.RevokeAsync(fixture.Request with { KeyId = "untrusted" }));
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await File.WriteAllTextAsync(fixture.Request.PrivateKeyPath, otherKey.ExportECPrivateKeyPem());
        await AssertCode("signing_key_not_trusted", () => ModelPublisher.RevokeAsync(fixture.Request));
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task RevokeCannotOverwriteLiveMetadataOrAnExistingOutputDirectory()
    {
        using var fixture = new Fixture();
        await AssertCode("output_exists_or_overlaps", () => ModelPublisher.RevokeAsync(fixture.Request with
        {
            OutputRoot = Path.Combine(fixture.SourceRoot, "new-revocation")
        }));
        Directory.CreateDirectory(fixture.Output);
        await AssertCode("output_exists_or_overlaps", () => ModelPublisher.RevokeAsync(fixture.Request));
    }

    private static async Task AssertCode(string code, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<ModelPublishException>(action);
        Assert.Equal(code, exception.Code);
    }

    private sealed class Fixture : IDisposable
    {
        internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mdr-" + Guid.NewGuid().ToString("N"));
        public string SourceRoot => Path.Combine(Root, "source");
        public string Output => Path.Combine(Root, "new-revocation");
        public string OutputCatalogPath => Path.Combine(Output, "metadata", "Staging", "catalog.json");
        public string QwenHash => ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.QwenArtifactId).Sha256;
        public ModelRevokeRequest Request { get; }
        public ModelCatalogTrustPolicy Policy { get; }
        public ModelCatalogTrustState SourceState { get; }

        public Fixture(bool existingQwenRevocation = false)
        {
            var path = Path.Combine(SourceRoot, "metadata", "Staging", "catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var keys = new Dictionary<string, string> { ["test-key"] = _key.ExportSubjectPublicKeyInfoPem() };
            Policy = new("test-models", "Staging", "win-x64", keys);
            var catalog = new ModelDistributionCatalog(1, "test-models", "Staging", "win-x64", 10,
                DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1),
                ApprovedModelArtifactPins.All.Where(artifact => !existingQwenRevocation || artifact.Sha256 != QwenHash).ToArray(),
                existingQwenRevocation ? [QwenHash] : null);
            var envelope = ModelCatalogSigner.CreateEnvelope(JsonSerializer.SerializeToUtf8Bytes(catalog, Json), "test-key", _key);
            File.WriteAllBytes(path, envelope);
            SourceState = ModelCatalogVerifier.Verify(envelope, Policy, null, DateTimeOffset.UtcNow).NextState;
            var trustPath = Path.Combine(SourceRoot, "public-trust-root.json");
            File.WriteAllBytes(trustPath, JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, repositoryId = "test-models", publicKeys = keys }, Json));
            var keyPath = Path.Combine(Root, "external-test-key.pem");
            File.WriteAllText(keyPath, _key.ExportECPrivateKeyPem());
            Request = new(path, Convert.ToHexString(SHA256.HashData(envelope)).ToLowerInvariant(), trustPath, Output, 11, [QwenHash], "test-key", keyPath);
        }

        public void Dispose()
        {
            _key.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }
}
