using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_TESTS.Updates;

public sealed class StaticModelArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-static-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Approved_release_pins_large_archive_and_exact_origin_without_a_public_descriptor_constructor()
    {
        var descriptor = ApprovedStaticModelBundle.Current;
        Assert.Equal("https://103.179.190.182:8443/models/VideoMaker-Models-20261016-082700.zip", descriptor.ArchiveUrl.AbsoluteUri);
        Assert.Equal(2_847_980_994L, descriptor.ArchiveSizeBytes);
        Assert.Equal("e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471", descriptor.ArchiveSha256);
        Assert.Equal("2f387304020b06c9c59747eb3cbf3dda08e4b029eafd49d4c77581c4154d4843", descriptor.InventorySha256);
        Assert.Empty(typeof(StaticModelBundleDescriptor).GetConstructors());
        Assert.All(descriptor.Artifacts, ApprovedModelArtifactPins.Validate);
        Assert.Equal("1.2.1", descriptor.RequiredWorkerVersion);
        Assert.Equal(3, descriptor.RequiredWorkerProtocolVersion);
    }

    [Fact]
    public async Task Import_verifies_all_entries_preserves_attribution_and_selects_only_runtime_artifacts()
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        var payload = await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal(fixture.BytesFor(ApprovedModelArtifactPins.QwenArtifactId), await File.ReadAllBytesAsync(payload.QwenModelPath));
        Assert.Equal(fixture.BytesFor(ApprovedModelArtifactPins.PiperArchiveArtifactId), await File.ReadAllBytesAsync(Path.Combine(payload.PiperBundleDirectory, "piper-offline.zip")));
        Assert.True(File.Exists(Path.Combine(payload.PayloadDirectory, "licenses", "NOTICE.txt")));
        Assert.True(File.Exists(Path.Combine(payload.PayloadDirectory, "provenance", "SOURCE.md")));
        Assert.False(Directory.Exists(Path.Combine(payload.PayloadDirectory, "artifacts")));
        Assert.Empty(Directory.EnumerateFiles(payload.PayloadDirectory, "*READY*", SearchOption.AllDirectories));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Fully_verified_payload_can_be_reused_without_an_archive_or_any_network()
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        var first = await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        File.Delete(fixture.ArchivePath(_root));
        var second = await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Disk_estimate_is_readonly_and_credits_only_verified_cache()
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        var empty = await store.EstimateAdditionalDiskBytesAsync(_root, CancellationToken.None);
        Assert.Equal(fixture.Archive.LongLength + fixture.Descriptor.MaximumExpandedBytes, empty);
        Assert.False(Directory.Exists(_root));
        Assert.Equal(0, handler.Count);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ArchivePath(_root))!);
        await File.WriteAllBytesAsync(fixture.ArchivePath(_root), fixture.Archive);
        Assert.Equal(fixture.Descriptor.MaximumExpandedBytes, await store.EstimateAdditionalDiskBytesAsync(_root, CancellationToken.None));
        var payload = await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal(0, await store.EstimateAdditionalDiskBytesAsync(_root, CancellationToken.None));
        await File.WriteAllTextAsync(payload.QwenModelPath, "unverified");
        Assert.Equal(fixture.Descriptor.MaximumExpandedBytes, await store.EstimateAdditionalDiskBytesAsync(_root, CancellationToken.None));
        Assert.Equal(0, handler.Count);
    }

    [Theory]
    [InlineData("qwen")]
    [InlineData("license")]
    [InlineData("unexpected")]
    public async Task Tampered_payload_is_replaced_from_hash_verified_archive(string mutation)
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        var payload = await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        var target = mutation switch
        {
            "qwen" => payload.QwenModelPath,
            "license" => Path.Combine(payload.PayloadDirectory, "licenses", "NOTICE.txt"),
            _ => Path.Combine(payload.PayloadDirectory, "READY.json")
        };
        await File.WriteAllTextAsync(target, "modified fixture");
        var repaired = await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal(fixture.BytesFor(ApprovedModelArtifactPins.QwenArtifactId), await File.ReadAllBytesAsync(repaired.QwenModelPath));
        Assert.Equal("fixture license retained", await File.ReadAllTextAsync(Path.Combine(repaired.PayloadDirectory, "licenses", "NOTICE.txt")));
        Assert.False(File.Exists(Path.Combine(repaired.PayloadDirectory, "READY.json")));
        Assert.Equal(1, handler.Count);
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("/absolute.exe")]
    [InlineData("C:/drive.exe")]
    [InlineData("nested\\escape.exe")]
    [InlineData("nested/CON.txt")]
    [InlineData("nested/trailing. ")]
    public async Task Zip_paths_are_rejected_before_extracting_any_file(string entryPath)
    {
        var fixture = StaticArchiveFixture.Create(entries => entries.Add(new(entryPath, [1, 2, 3])));
        await AssertRejected(fixture);
        Assert.False(File.Exists(Path.Combine(_root, "escape.exe")));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("case")]
    [InlineData("symlink")]
    [InlineData("dos-reparse")]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("wrong-inventory")]
    [InlineData("wrong-entry-hash")]
    [InlineData("wrong-checksums")]
    [InlineData("wrong-inventory-checksum")]
    public async Task Malformed_archives_fail_closed_and_never_promote_a_partial_payload(string mutation)
    {
        var fixture = StaticArchiveFixture.Create(entries =>
        {
            var license = entries.Single(e => e.Path == "licenses/NOTICE.txt");
            switch (mutation)
            {
                case "duplicate": entries.Add(license); break;
                case "case": entries.Add(license with { Path = "LICENSES/NOTICE.TXT" }); break;
                case "symlink": entries[entries.IndexOf(license)] = license with { Attributes = unchecked((int)0xa1ff0000) }; break;
                case "dos-reparse": entries[entries.IndexOf(license)] = license with { Attributes = 0x400 }; break;
                case "extra": entries.Add(new("unknown.exe", [5])); break;
                case "missing": entries.Remove(license); break;
                case "wrong-inventory": entries[entries.FindIndex(e => e.Path == "inventory.json")] = new("inventory.json", "{}"u8.ToArray()); break;
                case "wrong-entry-hash": entries[entries.IndexOf(license)] = license with { Bytes = Encoding.UTF8.GetBytes("other license retained") }; break;
                case "wrong-checksums": entries[entries.FindIndex(e => e.Path == "checksums.sha256")] = new("checksums.sha256", "invalid"u8.ToArray()); break;
                case "wrong-inventory-checksum":
                    var index = entries.FindIndex(e => e.Path == "checksums.sha256");
                    var text = Encoding.UTF8.GetString(entries[index].Bytes);
                    var inventoryLine = text.Split('\n').Single(l => l.EndsWith("  inventory.json"));
                    entries[index] = entries[index] with { Bytes = Encoding.UTF8.GetBytes(text.Replace(inventoryLine, new string('0', 64) + "  inventory.json")) };
                    break;
            }
        });
        await AssertRejected(fixture);
    }

    [Theory]
    [InlineData("ratio")]
    [InlineData("expanded")]
    [InlineData("count")]
    public async Task Zip_resource_limits_are_applied_before_payload_materialization(string mutation)
    {
        var fixture = StaticArchiveFixture.Create(entries =>
        {
            if (mutation == "ratio") entries.Add(new("bomb.txt", new byte[80_000], Compression: CompressionLevel.SmallestSize));
            if (mutation == "count") for (var i = 0; i < 65; i++) entries.Add(new($"entry-{i}.txt", [0]));
        }, maximumExpandedBytes: mutation == "expanded" ? 20 : 1_000_000);
        await AssertRejected(fixture);
    }

    [Fact]
    public async Task Existing_cache_lock_blocks_a_second_install_before_outbound()
    {
        var fixture = StaticArchiveFixture.Create();
        Directory.CreateDirectory(Path.Combine(_root, fixture.Descriptor.ArchiveSha256));
        using var lease = new FileStream(Path.Combine(_root, fixture.Descriptor.ArchiveSha256, "setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        var error = await Assert.ThrowsAsync<ModelDistributionException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));
        Assert.Equal(ModelDistributionErrors.Busy, error.Code);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Insufficient_cache_disk_prevents_outbound()
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        using var store = new StaticModelArchiveStore(http, fixture.Descriptor,
            checkDisk: (_, _) => throw new IOException("fixture disk full"));
        await Assert.ThrowsAsync<IOException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Extraction_disk_is_checked_separately_and_failure_keeps_verified_archive_for_retry()
    {
        var fixture = StaticArchiveFixture.Create();
        var checks = 0;
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        using var store = new StaticModelArchiveStore(http, fixture.Descriptor,
            checkDisk: (_, _) => { if (++checks == 3) throw new IOException("fixture extract disk full"); });
        await Assert.ThrowsAsync<IOException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));
        Assert.True(File.Exists(fixture.ArchivePath(_root)));
        Assert.False(Directory.Exists(fixture.PayloadPath(_root)));
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(fixture.ArchivePath(_root))!, "stage-*"));
    }

    private async Task AssertRejected(StaticArchiveFixture fixture)
    {
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));
        Assert.False(Directory.Exists(fixture.PayloadPath(_root)));
    }

    public void Dispose() => StaticArchiveFixture.DeleteTemp(_root);
}

internal sealed class StaticArchiveFixture
{
    internal sealed record Entry(string Path, byte[] Bytes, int Attributes = 0, CompressionLevel Compression = CompressionLevel.NoCompression);
    internal required byte[] Archive { get; init; }
    internal required StaticModelBundleDescriptor Descriptor { get; init; }
    internal required IReadOnlyDictionary<string, byte[]> ArtifactBytes { get; init; }

    internal static StaticArchiveFixture Create(Action<List<Entry>>? mutate = null, long maximumExpandedBytes = 1_000_000)
    {
        var bytes = new Dictionary<string, byte[]>
        {
            [ApprovedModelArtifactPins.QwenArtifactId] = "fixture Qwen weights"u8.ToArray(),
            [ApprovedModelArtifactPins.PiperArchiveArtifactId] = "fixture Piper archive"u8.ToArray(),
            [ApprovedModelArtifactPins.PiperManifestArtifactId] = "fixture Piper manifest"u8.ToArray()
        };
        var artifacts = ApprovedModelArtifactPins.All.Select(pin => pin with
        {
            SizeBytes = bytes[pin.ArtifactId].LongLength,
            Sha256 = Hash(bytes[pin.ArtifactId]),
            RelativePath = $"artifacts/{pin.ComponentId}/{pin.ComponentVersion}/{Hash(bytes[pin.ArtifactId])}/{pin.FileName}"
        }).ToArray();
        var entries = artifacts.Select(a => new Entry(a.RelativePath, bytes[a.ArtifactId])).ToList();
        entries.Add(new("licenses/NOTICE.txt", "fixture license retained"u8.ToArray()));
        entries.Add(new("provenance/SOURCE.md", "fixture origin retained"u8.ToArray()));
        entries.Add(new("artifacts/ocr/build.nupkg", "fixture build input"u8.ToArray()));
        entries.Add(new("compatibility.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, candidateId = "fixture-static-v1",
            desktop = new { version = "1.0.0", buildNumber = 1, platform = "win-x64", runtimeQualified = false },
            components = new object[]
            {
                new { componentId = artifacts[0].ComponentId, componentVersion = artifacts[0].ComponentVersion,
                    modelSha256 = artifacts[0].Sha256, workerVersion = "1.2.1", protocolVersion = 3 },
                new { componentId = artifacts[1].ComponentId, componentVersion = artifacts[1].ComponentVersion,
                    protocolVersion = 1, createFreshVenvOnTarget = true },
                new { componentId = "paddleocr-en-zh-win-x64", componentVersion = "3.3.1",
                    runtimeDelivery = "matching-desktop-package", independentRuntimeDownloadSupported = false }
            }
        })));
        var inventory = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            candidateId = "fixture-static-v1",
            files = entries.Select(e => new { path = e.Path, sizeBytes = e.Bytes.LongLength, sha256 = Hash(e.Bytes) }).ToArray()
        });
        var checksum = string.Join("\n", entries.Select(e => Hash(e.Bytes) + "  " + e.Path)) + "\n" + Hash(inventory) + "  inventory.json\n";
        entries.Add(new("inventory.json", inventory));
        entries.Add(new("checksums.sha256", Encoding.UTF8.GetBytes(checksum)));
        mutate?.Invoke(entries);
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var source in entries)
            {
                var entry = zip.CreateEntry(source.Path, source.Compression);
                entry.ExternalAttributes = source.Attributes;
                using var target = entry.Open();
                target.Write(source.Bytes);
            }
        var archive = memory.ToArray();
        return new()
        {
            Archive = archive,
            ArtifactBytes = bytes,
            Descriptor = new("fixture-static-v1", new("https://models.invalid:8443/models/fixture.zip"),
                archive.LongLength, Hash(archive), Hash(inventory), maximumExpandedBytes, artifacts)
        };
    }

    internal byte[] BytesFor(string id) => ArtifactBytes[id];
    internal string ArchivePath(string root) => Path.Combine(root, Descriptor.ArchiveSha256, "archive.zip");
    internal string PayloadPath(string root) => Path.Combine(root, Descriptor.ArchiveSha256, "payload");
    internal StaticModelArchiveStore Store(HttpClient http) => new(http, Descriptor,
        checkDisk: (_, _) => { }, delayAsync: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
    internal FixtureHttpHandler Handler(Func<HttpRequestMessage, int, HttpResponseMessage>? callback = null) =>
        new(callback ?? ((request, _) => Response(request, HttpStatusCode.OK, Archive)));

    internal static HttpResponseMessage Response(HttpRequestMessage request, HttpStatusCode status, byte[] bytes)
    {
        var response = new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent(bytes) };
        response.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\"");
        response.Content.Headers.ContentType = new("application/zip");
        return response;
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static void DeleteTemp(string root)
    {
        var full = Path.GetFullPath(root);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(full).Length < 20)
            throw new InvalidOperationException("Test cleanup escaped the owned temporary directory.");
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }
}

internal sealed class FixtureHttpHandler(Func<HttpRequestMessage, int, HttpResponseMessage> callback) : HttpMessageHandler
{
    internal int Count => Offsets.Count;
    internal List<long?> Offsets { get; } = [];
    internal List<string?> IfRanges { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = Count;
        Offsets.Add(request.Headers.Range?.Ranges.Single().From);
        IfRanges.Add(request.Headers.IfRange?.EntityTag?.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.Equal("identity", request.Headers.AcceptEncoding.Single().Value);
        return Task.FromResult(callback(request, index));
    }
}
