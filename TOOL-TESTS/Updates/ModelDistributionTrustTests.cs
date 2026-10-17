using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_TESTS.Updates;

public sealed class ModelDistributionTrustTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Valid_signed_payload_returns_exact_digest_and_next_state()
    {
        var payload = Payload(Catalog());
        var result = Verify(payload);
        Assert.Equal(7, result.Catalog.Sequence);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), result.CatalogSha256);
        Assert.Equal(result.CatalogSha256, result.NextState.CatalogSha256);
        Assert.Equal("fixture-models", result.NextState.RepositoryId);
        Assert.Equal(3, result.Catalog.Artifacts.Count);
    }

    [Fact]
    public void Signature_covers_exact_bytes_including_whitespace()
    {
        var envelope = Envelope(Payload(Catalog()));
        var changed = envelope with { PayloadBase64 = Convert.ToBase64String(Payload(Catalog()).Concat(new byte[] { 32 }).ToArray()) };
        Error(ModelDistributionErrors.SignatureInvalid, () => VerifyEnvelope(changed));
    }

    [Fact]
    public void Signed_whitespace_is_supported_without_canonicalizing_json()
    {
        var payload = Encoding.UTF8.GetBytes("\n " + Encoding.UTF8.GetString(Payload(Catalog())) + " \n");
        var result = Verify(payload);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), result.CatalogSha256);
    }

    [Fact]
    public void Empty_trust_root_is_fail_closed()
    {
        var policy = Policy() with { PublicKeys = new Dictionary<string, string>() };
        Error(ModelDistributionErrors.TrustNotConfigured,
            () => ModelCatalogVerifier.Verify(Signed(Payload(Catalog())), policy, null, Now));
    }

    [Fact]
    public void Private_key_configuration_is_not_accepted_as_a_public_root()
    {
        var policy = Policy() with { PublicKeys = new Dictionary<string, string> { ["fixture"] = _key.ExportPkcs8PrivateKeyPem() } };
        Error(ModelDistributionErrors.TrustNotConfigured,
            () => ModelCatalogVerifier.Verify(Signed(Payload(Catalog())), policy, null, Now));
    }

    [Fact]
    public void Unknown_or_replaced_key_cannot_authorize_metadata()
    {
        var envelope = Envelope(Payload(Catalog()));
        Error(ModelDistributionErrors.SignatureInvalid, () => VerifyEnvelope(envelope with { KeyId = "unknown" }));
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var policy = Policy() with { PublicKeys = new Dictionary<string, string> { ["fixture"] = other.ExportSubjectPublicKeyInfoPem() } };
        Error(ModelDistributionErrors.SignatureInvalid,
            () => ModelCatalogVerifier.Verify(Signed(Payload(Catalog())), policy, null, Now));
    }

    [Fact]
    public void Algorithm_substitution_and_signature_corruption_are_rejected()
    {
        var envelope = Envelope(Payload(Catalog()));
        Error(ModelDistributionErrors.CatalogInvalid, () => VerifyEnvelope(envelope with { Algorithm = "none" }));
        var signature = Convert.FromBase64String(envelope.SignatureBase64);
        signature[0] ^= 1;
        Error(ModelDistributionErrors.SignatureInvalid,
            () => VerifyEnvelope(envelope with { SignatureBase64 = Convert.ToBase64String(signature) }));
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("channel")]
    [InlineData("platform")]
    public void Metadata_cannot_be_replayed_into_another_repository_channel_or_platform(string change)
    {
        var catalog = Catalog();
        catalog = change switch
        {
            "repository" => catalog with { RepositoryId = "another-repository" },
            "channel" => catalog with { Channel = "Beta" },
            _ => catalog with { Platform = "linux-x64" }
        };
        Error(ModelDistributionErrors.CatalogInvalid, () => Verify(Payload(catalog)));
    }

    [Fact]
    public void Expiry_is_checked_including_the_exact_expiry_instant()
    {
        Error(ModelDistributionErrors.CatalogExpired,
            () => Verify(Payload(Catalog() with { ExpiresAtUtc = Now })));
    }

    [Fact]
    public void Excessive_validity_or_future_publication_is_rejected()
    {
        Error(ModelDistributionErrors.CatalogInvalid,
            () => Verify(Payload(Catalog() with { ExpiresAtUtc = Now.AddDays(31) })));
        Error(ModelDistributionErrors.CatalogInvalid,
            () => Verify(Payload(Catalog() with { PublishedAtUtc = Now.AddMinutes(6) })));
    }

    [Fact]
    public void High_water_state_blocks_rollback_and_same_sequence_equivocation()
    {
        var first = Verify(Payload(Catalog()));
        Verify(Payload(Catalog()), first.NextState);
        Error(ModelDistributionErrors.CatalogRollback,
            () => Verify(Payload(Catalog() with { Sequence = 6 }), first.NextState));
        Error(ModelDistributionErrors.CatalogRollback,
            () => Verify(Payload(Catalog() with { ExpiresAtUtc = Now.AddDays(2) }), first.NextState));
        var next = Verify(Payload(Catalog() with { Sequence = 8 }), first.NextState);
        Assert.Equal(8, next.NextState.Sequence);
    }

    [Fact]
    public void Deliberate_operational_rollback_uses_new_sequence_and_approved_old_bytes()
    {
        var first = Verify(Payload(Catalog()));
        var next = Verify(Payload(Catalog() with { Sequence = 8, Artifacts = [ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.QwenArtifactId)] }), first.NextState);
        Assert.Equal(8, next.NextState.Sequence);
        Assert.Single(next.Catalog.Artifacts);
    }

    [Fact]
    public void Trust_state_from_another_repository_is_not_accepted_or_overwritten()
    {
        var state = Verify(Payload(Catalog())).NextState with { RepositoryId = "other" };
        Error(ModelDistributionErrors.CatalogInvalid, () => Verify(Payload(Catalog()), state));
        Assert.Equal("other", state.RepositoryId);
        Assert.Equal(7, state.Sequence);
    }

    [Fact]
    public void Signed_catalog_cannot_authorize_unapproved_or_altered_model_bytes()
    {
        var approved = ApprovedModelArtifactPins.All[0];
        var altered = approved with { Sha256 = new string('a', 64) };
        Error(ModelDistributionErrors.ArtifactNotApproved,
            () => Verify(Payload(Catalog() with { Artifacts = [altered] })));
        Error(ModelDistributionErrors.ArtifactNotApproved,
            () => Verify(Payload(Catalog() with { Artifacts = [approved with { ArtifactId = "new-model" }] })));
    }

    [Theory]
    [InlineData("../escape.gguf")]
    [InlineData("/root/model.gguf")]
    [InlineData("artifacts/../staging/model.gguf")]
    [InlineData("https://attacker.invalid/model.gguf")]
    [InlineData("artifacts/model.gguf:stream")]
    public void Even_validly_signed_paths_cannot_escape_the_pinned_target(string path)
    {
        var artifact = ApprovedModelArtifactPins.All[0] with { RelativePath = path };
        Error(ModelDistributionErrors.ArtifactNotApproved,
            () => Verify(Payload(Catalog() with { Artifacts = [artifact] })));
    }

    [Fact]
    public void Incompatible_worker_and_reduced_disk_requirements_are_rejected()
    {
        var artifact = ApprovedModelArtifactPins.All[0];
        Error(ModelDistributionErrors.ArtifactNotApproved,
            () => Verify(Payload(Catalog() with { Artifacts = [artifact with { RequiredWorkerProtocolVersion = 1 }] })));
        Error(ModelDistributionErrors.ArtifactNotApproved,
            () => Verify(Payload(Catalog() with { Artifacts = [artifact with { MinimumFreeDiskBytes = 1 }] })));
    }

    [Fact]
    public void Duplicate_targets_and_duplicate_json_members_are_rejected()
    {
        var artifact = ApprovedModelArtifactPins.All[0];
        Error(ModelDistributionErrors.CatalogInvalid,
            () => Verify(Payload(Catalog() with { Artifacts = [artifact, artifact] })));
        var duplicate = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Payload(Catalog()))
            .Replace("\"sequence\":7", "\"sequence\":7,\"sequence\":8", StringComparison.Ordinal));
        Error(ModelDistributionErrors.CatalogInvalid, () => Verify(duplicate));
    }

    [Fact]
    public void Unknown_signed_fields_and_oversized_envelope_are_rejected()
    {
        var unknown = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Payload(Catalog()))
            .Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"runCommand\":\"anything\"", StringComparison.Ordinal));
        Error(ModelDistributionErrors.CatalogInvalid, () => Verify(unknown));
        Error(ModelDistributionErrors.CatalogInvalid,
            () => ModelCatalogVerifier.Verify(new byte[ModelCatalogVerifier.MaximumEnvelopeBytes + 1], Policy(), null, Now));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("not json")]
    public void Malformed_inputs_have_bounded_safe_errors(string input)
    {
        var error = Error(ModelDistributionErrors.CatalogInvalid,
            () => ModelCatalogVerifier.Verify(Encoding.UTF8.GetBytes(input), Policy(), null, Now));
        Assert.True(error.Message.Length < 200);
        Assert.DoesNotContain(input, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Distribution_pins_match_current_runtime_approved_definitions()
    {
        var qwen = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.QwenArtifactId);
        var source = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        Assert.Equal(source.ComponentId, qwen.ComponentId);
        Assert.Equal(source.ModelSizeBytes, qwen.SizeBytes);
        Assert.Equal(source.ModelSha256, qwen.Sha256);
        Assert.Equal(source.EngineVersion, qwen.ComponentVersion);
        Assert.Equal(VietsubTranslationWorkerProtocol.WorkerVersion, qwen.RequiredWorkerVersion);
        Assert.Equal(VietsubTranslationWorkerProtocol.Version, qwen.RequiredWorkerProtocolVersion);
        var piper = PiperOfflineBundle.ApprovedDefinition();
        Assert.Equal(piper.ArchiveSize, ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.PiperArchiveArtifactId).SizeBytes);
        Assert.Equal(piper.ArchiveSha256, ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.PiperArchiveArtifactId).Sha256);
        Assert.Equal(piper.ManifestSize, ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.PiperManifestArtifactId).SizeBytes);
        Assert.Equal(piper.ManifestSha256, ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.PiperManifestArtifactId).Sha256);
    }

    [Fact]
    public void Revocation_is_explicit_known_unique_and_cannot_also_be_active()
    {
        var hash = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.QwenArtifactId).Sha256;
        var revoked = Catalog() with { Artifacts = [], RevokedArtifactSha256 = [hash] };
        var result = Verify(Payload(revoked));
        Assert.Equal(hash, Assert.Single(result.Catalog.RevokedArtifactSha256!));
        Error(ModelDistributionErrors.CatalogInvalid,
            () => Verify(Payload(Catalog() with { RevokedArtifactSha256 = [hash] })));
        Error(ModelDistributionErrors.CatalogInvalid,
            () => Verify(Payload(revoked with { RevokedArtifactSha256 = [hash, hash] })));
        Error(ModelDistributionErrors.CatalogInvalid,
            () => Verify(Payload(revoked with { RevokedArtifactSha256 = [new string('f', 64)] })));
    }

    [Fact]
    public void Known_revocation_stays_sticky_when_later_catalog_omits_it()
    {
        var qwen = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.QwenArtifactId);
        var first = Verify(Payload(Catalog() with { Artifacts = [], RevokedArtifactSha256 = [qwen.Sha256] }));
        var next = Verify(Payload(Catalog() with { Sequence = 8, Artifacts = [] }), first.NextState);
        Assert.Equal(qwen.Sha256, Assert.Single(next.NextState.RevokedArtifactSha256!));
        Error(ModelDistributionErrors.ArtifactNotApproved,
            () => Verify(Payload(Catalog() with { Sequence = 9, Artifacts = [qwen] }), next.NextState));
        var serialized = JsonSerializer.SerializeToUtf8Bytes(next.NextState, Json);
        var reopened = JsonSerializer.Deserialize<ModelCatalogTrustState>(serialized, Json)!;
        Error(ModelDistributionErrors.ArtifactNotApproved,
            () => Verify(Payload(Catalog() with { Sequence = 9, Artifacts = [qwen] }), reopened));
    }

    private ModelDistributionCatalog Catalog() => new(1, "fixture-models", "Stable", "win-x64", 7,
        Now.AddHours(-1), Now.AddDays(1), ApprovedModelArtifactPins.All);
    private ModelCatalogTrustPolicy Policy() => new("fixture-models", "Stable", "win-x64",
        new Dictionary<string, string> { ["fixture"] = _key.ExportSubjectPublicKeyInfoPem() });
    private static byte[] Payload(ModelDistributionCatalog catalog) => JsonSerializer.SerializeToUtf8Bytes(catalog, Json);
    private byte[] Signed(byte[] payload) => ModelCatalogSigner.CreateEnvelope(payload, "fixture", _key);
    private ModelDistributionEnvelope Envelope(byte[] payload) => JsonSerializer.Deserialize<ModelDistributionEnvelope>(Signed(payload), Json)!;
    private VerifiedModelCatalog Verify(byte[] payload, ModelCatalogTrustState? previous = null) =>
        ModelCatalogVerifier.Verify(Signed(payload), Policy(), previous, Now);
    private VerifiedModelCatalog VerifyEnvelope(ModelDistributionEnvelope envelope) =>
        ModelCatalogVerifier.Verify(JsonSerializer.SerializeToUtf8Bytes(envelope, Json), Policy(), null, Now);
    private static ModelDistributionException Error(string code, Action action)
    {
        var error = Assert.Throws<ModelDistributionException>(action);
        Assert.Equal(code, error.Code);
        return error;
    }
    public void Dispose() => _key.Dispose();
}
