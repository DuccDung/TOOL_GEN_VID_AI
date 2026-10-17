using System.Security.Cryptography;
using System.Text.Json;
using TOOL_MODEL_PUBLISHER;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_TESTS.Updates;

public sealed class ModelPublisherTests
{
    [Fact]
    public async Task PublicKeyExport_NeverExportsPrivateMaterialOrOverwritesAnExistingFile()
    {
        using var fixture = new Fixture();
        var output = Path.Combine(fixture.Root, "public.pem");
        await ModelPublisher.ExportPublicKeyAsync(fixture.KeyPath, output);
        var pem = await File.ReadAllTextAsync(output);
        Assert.Contains("BEGIN PUBLIC KEY", pem);
        Assert.DoesNotContain("PRIVATE", pem);
        using var exported = ECDsa.Create();
        exported.ImportFromPem(pem);
        Assert.Equal(fixture.PublicKey, exported.ExportSubjectPublicKeyInfoPem());
        await AssertCode("output_exists_or_invalid", () => ModelPublisher.ExportPublicKeyAsync(fixture.KeyPath, output));
    }

    [Fact]
    public void Stage_RejectsKeysInsideTheHandoverAndOutputDirectories()
    {
        using var fixture = new Fixture();
        var inner = fixture.Request with { PrivateKeyPath = Path.Combine(fixture.Handover, "secret.pem") };
        Assert.Equal("private_key_must_be_external", Assert.Throws<ModelPublishException>(() => ModelPublisher.ValidateRequest(inner)).Code);
        var output = fixture.Request with { PrivateKeyPath = Path.Combine(fixture.Output, "secret.pem") };
        Assert.Equal("private_key_must_be_external", Assert.Throws<ModelPublishException>(() => ModelPublisher.ValidateRequest(output)).Code);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Theory]
    [InlineData("Stable")]
    [InlineData("Beta")]
    public void ProductionChannel_RequiresAnExternalQualificationReport(string channel)
    {
        using var fixture = new Fixture();
        var request = fixture.Request with { Channel = channel };
        Assert.Equal("production_qualification_required", Assert.Throws<ModelPublishException>(() => ModelPublisher.ValidateRequest(request)).Code);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public void Stage_NeverOverwritesExistingCandidatesOrWritesInsideTheHandover()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Output);
        var existing = Path.Combine(fixture.Output, "keep.txt");
        File.WriteAllText(existing, "keep");
        Assert.Throws<ModelPublishException>(() => ModelPublisher.ValidateRequest(fixture.Request));
        Assert.Equal("keep", File.ReadAllText(existing));
        Assert.Throws<ModelPublishException>(() => ModelPublisher.ValidateRequest(fixture.Request with
        {
            OutputRoot = Path.Combine(fixture.Handover, "nested")
        }));
    }

    [Fact]
    public async Task InventoryHashMustMatchBeforeCreatingStagingFiles()
    {
        using var fixture = new Fixture();
        await AssertCode("inventory_hash_mismatch", () => ModelPublisher.StageAsync(fixture.Request with { InventorySha256 = new string('0', 64) }));
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task InventoryCannotSubstituteDifferentModelBytesOrPaths()
    {
        using var fixture = new Fixture();
        var bytes = File.ReadAllBytes(fixture.InventoryPath);
        var text = System.Text.Encoding.UTF8.GetString(bytes).Replace("Qwen3-4B-Q4_K_M.gguf", "other.gguf", StringComparison.Ordinal);
        await File.WriteAllTextAsync(fixture.InventoryPath, text);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture.InventoryPath))).ToLowerInvariant();
        await AssertCode("inventory_artifact_mismatch", () => ModelPublisher.StageAsync(fixture.Request with { InventorySha256 = hash }));
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task BrokenPayloadCannotCreateSignedMetadata()
    {
        using var fixture = new Fixture();
        var qwen = ApprovedModelArtifactPins.Get(ApprovedModelArtifactPins.QwenArtifactId);
        var bad = Path.Combine(fixture.Handover, qwen.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(bad)!);
        await File.WriteAllBytesAsync(bad, [1, 2, 3]);
        await AssertCode("source_artifact_invalid", () => ModelPublisher.StageAsync(fixture.Request));
        Assert.True(File.Exists(Path.Combine(fixture.Output, ".incomplete")));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "metadata", "Staging", "catalog.json")));
        Assert.Empty(Directory.GetFiles(fixture.Output, "*.pem", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task StageRequiresTheSameDesktopBuildAsTheInventoriedHandover()
    {
        using var fixture = new Fixture();
        await AssertCode("desktop_compatibility_mismatch", () => ModelPublisher.StageAsync(fixture.Request with { DesktopBuildNumber = 2 }));
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task QualificationCannotBeForgedFromAnArtifactVerificationMarker()
    {
        using var fixture = new Fixture();
        var report = Path.Combine(fixture.Root, "qualification.json");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, candidateId = "candidate-test", inventorySha256 = fixture.Request.InventorySha256,
            desktopVersion = "1.0.0", desktopBuildNumber = 1, runtimeQualified = false, distributionApproved = false,
            evidence = Array.Empty<object>()
        }));
        await AssertCode("qualification_invalid", () => ModelPublisher.StageAsync(fixture.Request with
        {
            Channel = "Stable", QualificationFile = report
        }));
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Fact]
    public async Task CliFailureDoesNotPrintPrivateKeyDataOrInputPaths()
    {
        using var fixture = new Fixture();
        const string sensitive = "INVALID_PRIVATE_CONTENT_NEVER_PRINT";
        await File.WriteAllTextAsync(fixture.KeyPath, sensitive);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await ModelPublisherCommand.RunAsync(["public-key", "--private-key", fixture.KeyPath,
            "--output", Path.Combine(fixture.Root, "public.pem")], output, error);
        Assert.Equal(1, exit);
        Assert.DoesNotContain(sensitive, output.ToString() + error);
        Assert.DoesNotContain(fixture.Root, output.ToString() + error);
        Assert.Contains("private_key_invalid", error.ToString());
    }

    [Fact]
    public async Task CliRejectsUnknownAndDuplicateOptions()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await ModelPublisherCommand.RunAsync(["stage", "--unsafe", "value"], output, error));
        Assert.Equal(1, await ModelPublisherCommand.RunAsync(["stage", "--output", "a", "--output", "b"], output, error));
    }

    private static async Task AssertCode(string expected, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<ModelPublishException>(action);
        Assert.Equal(expected, exception.Code);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mdp-" + Guid.NewGuid().ToString("N"));
        public string Handover => Path.Combine(Root, "handover");
        public string Output => Path.Combine(Root, "staged");
        public string InventoryPath => Path.Combine(Handover, "inventory.json");
        public string KeyPath => Path.Combine(Root, "external-test-key.pem");
        public string PublicKey { get; }
        public ModelPublishRequest Request { get; }

        public Fixture()
        {
            Directory.CreateDirectory(Handover);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            File.WriteAllText(KeyPath, key.ExportECPrivateKeyPem());
            PublicKey = key.ExportSubjectPublicKeyInfoPem();
            var compatibilityBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                candidateId = "candidate-test", desktop = new { version = "1.0.0", buildNumber = 1, platform = "win-x64" }
            });
            File.WriteAllBytes(Path.Combine(Handover, "compatibility.json"), compatibilityBytes);
            var files = ApprovedModelArtifactPins.All.Select(artifact => new
            {
                path = artifact.RelativePath, artifact.SizeBytes, artifact.Sha256, artifact.ComponentId,
                artifact.ComponentVersion, artifact.ArtifactId
            }).ToList();
            files.Add(new
            {
                path = "compatibility.json", SizeBytes = (long)compatibilityBytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(compatibilityBytes)).ToLowerInvariant(), ComponentId = "",
                ComponentVersion = "", ArtifactId = ""
            });
            var inventory = new
            {
                schemaVersion = 1, candidateId = "candidate-test", state = "ArtifactVerified",
                files
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(inventory, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.WriteAllBytes(InventoryPath, bytes);
            Request = new(Handover, Output, "test-models", "Staging", 1, "1.0.0", 1, "test-key", KeyPath,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
