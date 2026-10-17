using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

// Small, inert ZIP fixtures verify the packaging trust boundary. They do not run an
// application, install a model or qualify the bundle on a clean Windows machine.
public sealed class RawDesktopPackageSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-raw-package-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Generated = ["update-manifest.json", "distribution-profile.json", "package-inventory.json"];
    private static readonly Uri Api = new("https://metadata.invalid/");

    [Theory]
    [InlineData("")]
    [InlineData("publish/")]
    [InlineData("Ban phat hanh 01/")]
    public async Task Raw_zip_is_flattened_and_internal_metadata_is_materialized_after_verification(string prefix)
    {
        var f = Fixture(prefix);
        using (var memory = new MemoryStream(f.Bytes))
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Read))
        {
            var layout = DesktopPackageMetadataPolicy.InspectLayout(zip);
            Assert.Equal(prefix, layout.PayloadRoot);
            Assert.Equal(f.Document.ExpandedSizeBytes, layout.ExpandedSizeBytes);
            Assert.Equal(f.Document.EntryCount, layout.EntryCount);
            Assert.Equal(f.Document.Files.Select(x => x.Path).Order(), layout.Files.Keys.Order());
        }
        var stage = Path.Combine(_root, "stage");
        await DesktopPackageMetadataPolicy.ExtractAndMaterializeAsync(f.Archive, stage, f.Release, f.Document, default);
        if (prefix.Length > 0) Assert.False(Directory.Exists(Path.Combine(stage, prefix.TrimEnd('/'))));
        Assert.All(f.Payload, pair => Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(stage, pair.Key.Replace('/', Path.DirectorySeparatorChar)))));

        var manifest = JsonSerializer.Deserialize<DesktopUpdateManifest>(File.ReadAllBytes(Path.Combine(stage, Generated[0])), Json)!;
        Assert.Equal(f.Document.Manifest.Version, manifest.Version);
        Assert.Equal(f.Document.Manifest.BuildNumber, manifest.BuildNumber);
        Assert.Equal(f.Payload.Keys.Concat(Generated).Order(), manifest.ManagedFiles.Order());
        Assert.Equal(f.Document.DistributionProfile,
            JsonSerializer.Deserialize<DesktopPackageDistributionProfile>(File.ReadAllBytes(Path.Combine(stage, Generated[1])), Json));
        using var inventory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stage, Generated[2])));
        Assert.Equal(1, inventory.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(new[] { Generated[2] }, inventory.RootElement.GetProperty("excludes").EnumerateArray().Select(x => x.GetString()));
        var entries = inventory.RootElement.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(f.Payload.Count + 2, entries.Length);
        Assert.DoesNotContain(entries, x => x.GetProperty("path").GetString() == Generated[2]);
        foreach (var entry in entries)
        {
            var bytes = File.ReadAllBytes(Path.Combine(stage, entry.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(bytes.LongLength, entry.GetProperty("sizeBytes").GetInt64());
            Assert.Equal(Hash(bytes), entry.GetProperty("sha256").GetString());
        }
        Assert.Equal(f.Bytes, File.ReadAllBytes(f.Archive));
    }

    [Fact]
    public async Task Explicit_directory_entries_are_not_confused_with_payload_files_or_generated_inventory()
    {
        var f = Fixture("publish/");
        var bytes = Archive(f.Payload, "publish/", [("publish/", [], null), ("publish/tools/", [], null), ("publish/tools/ffmpeg/", [], null)]);
        File.WriteAllBytes(f.Archive, bytes);
        var document = f.Document with { ArchiveSha256 = Hash(bytes), EntryCount = f.Document.EntryCount + 3 };
        var release = BindDocument(f.Release with { Sha256 = document.ArchiveSha256, SizeBytes = bytes.LongLength }, document);
        var stage = Path.Combine(_root, "directories");
        await DesktopPackageMetadataPolicy.ExtractAndMaterializeAsync(f.Archive, stage, release, document, default);
        Assert.Equal(f.Payload.Count + Generated.Length, Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(f.Payload["TOOL-LOCAL.exe"], File.ReadAllBytes(Path.Combine(stage, "TOOL-LOCAL.exe")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("publish/../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("publish\\escape.txt")]
    [InlineData("publish/file:stream")]
    [InlineData("publish/CON.txt")]
    [InlineData("publish/nul/file.txt")]
    [InlineData("publish/file.")]
    [InlineData("publish/file ")]
    [InlineData("publish//file.txt")]
    public void Unsafe_archive_paths_are_rejected_before_selecting_a_payload_root(string path)
    {
        using var memory = new MemoryStream(Archive(Payload(), "publish/", [(path, "bad"u8.ToArray(), null)]));
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => DesktopPackageMetadataPolicy.InspectLayout(zip));
    }

    [Theory]
    [InlineData("outside.txt")]
    [InlineData("other/TOOL-LOCAL.exe")]
    [InlineData("TOOL-LOCAL.exe")]
    [InlineData("publish/TOOL-local.EXE")]
    [InlineData("publish/tools")]
    [InlineData("publish/tools/ffmpeg/ffmpeg.exe/child.txt")]
    public void Mixed_roots_duplicate_executables_and_file_directory_conflicts_are_rejected(string extra)
    {
        using var memory = new MemoryStream(Archive(Payload(), "publish/", [(extra, "untrusted"u8.ToArray(), null)]));
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => DesktopPackageMetadataPolicy.InspectLayout(zip));
    }

    [Theory]
    [InlineData(unchecked((int)0xA1FF0000))]
    [InlineData(0x400)]
    [InlineData(unchecked((int)0x61FF0000))]
    public void Symbolic_links_reparse_points_and_special_files_are_rejected(int attributes)
    {
        using var memory = new MemoryStream(Archive(Payload(), "publish/", [("publish/link.txt", "target"u8.ToArray(), attributes)]));
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => DesktopPackageMetadataPolicy.InspectLayout(zip));
    }

    [Fact]
    public void Expanded_size_limit_is_checked_without_allocating_the_advertised_payload()
    {
        var bytes = Archive(Payload(), "");
        var patched = 0;
        for (var i = 0; i <= bytes.Length - 46 && patched < 2; i++)
        {
            if (!bytes.AsSpan(i, 4).SequenceEqual(new byte[] { 0x50, 0x4b, 0x01, 0x02 })) continue;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 24, 4), uint.MaxValue - 1);
            patched++;
        }
        Assert.Equal(2, patched);
        using var memory = new MemoryStream(bytes);
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => DesktopPackageMetadataPolicy.InspectLayout(zip));
    }

    [Fact]
    public void Zip_without_an_application_has_no_payload_root()
    {
        using var memory = new MemoryStream(Archive(new() { ["readme.txt"] = "inert"u8.ToArray() }, "publish/"));
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => DesktopPackageMetadataPolicy.InspectLayout(zip));
    }

    [Fact]
    public void Metadata_round_trips_exact_archive_identity_and_relative_file_hashes()
    {
        var f = Fixture("publish/");
        var bytes = DesktopPackageMetadataPolicy.SerializeDocument(f.Document);
        var document = DesktopPackageMetadataPolicy.DeserializeDocument(bytes);
        DesktopPackageMetadataPolicy.ValidateDocument(document, f.Release);
        Assert.Equal(f.Document.ArchiveSha256, document.ArchiveSha256);
        Assert.Equal(f.Document.PayloadRoot, document.PayloadRoot);
        Assert.Equal(f.Document.Files, document.Files);
        Assert.Equal(f.Document.Manifest.ManagedFiles, document.Manifest.ManagedFiles);
    }

    [Theory]
    [InlineData("archive-hash")]
    [InlineData("schema")]
    [InlineData("payload-traversal")]
    [InlineData("payload-two-levels")]
    [InlineData("payload-no-slash")]
    [InlineData("duplicate")]
    [InlineData("case-duplicate")]
    [InlineData("file-path")]
    [InlineData("file-size")]
    [InlineData("file-hash")]
    [InlineData("generated-file")]
    [InlineData("unmanaged-file")]
    [InlineData("missing-generated")]
    [InlineData("expanded-size")]
    [InlineData("entry-count")]
    [InlineData("user-settings")]
    [InlineData("setup-receipt")]
    [InlineData("git-metadata")]
    [InlineData("node-modules")]
    [InlineData("profile-protocol")]
    [InlineData("profile-model")]
    [InlineData("minimum-bootstrapper")]
    public void Malformed_metadata_never_becomes_an_extraction_plan(string mutation)
    {
        var f = Fixture();
        var document = Mutate(f.Document, mutation);
        Assert.Throws<InvalidDataException>(() => DesktopPackageMetadataPolicy.ValidateDocument(document));
    }

    [Theory]
    [InlineData("TOOL-LOCAL.exe", "tool-local.exe")]
    [InlineData("appsettings.json", "APPSETTINGS.JSON")]
    public void Required_payload_files_must_be_nonempty_regardless_of_Windows_path_case(string original, string alias)
    {
        var value = Fixture().Document;
        var required = value.Files.Single(x => x.Path == original);
        var document = value with
        {
            Files = value.Files.Select(x => x.Path == original ? x with { Path = alias, SizeBytes = 0, Sha256 = Hash([]) } : x).ToArray(),
            Manifest = value.Manifest with { ManagedFiles = value.Manifest.ManagedFiles.Select(x => x == original ? alias : x).ToArray() },
            ExpandedSizeBytes = value.ExpandedSizeBytes - required.SizeBytes
        };
        Assert.Throws<InvalidDataException>(() => DesktopPackageMetadataPolicy.ValidateDocument(document));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("build")]
    [InlineData("api")]
    [InlineData("profile")]
    [InlineData("archive-hash")]
    [InlineData("platform")]
    [InlineData("model")]
    public void Valid_document_for_another_release_cannot_be_used_for_this_release(string mutation)
    {
        var f = Fixture();
        var document = mutation switch
        {
            "version" => f.Document with { Manifest = f.Document.Manifest with { Version = "2.0.0" } },
            "build" => f.Document with { Manifest = f.Document.Manifest with { BuildNumber = 8 } },
            "api" => f.Document with { DistributionProfile = f.Document.DistributionProfile with { ServerBaseUrl = "https://another.invalid/" } },
            "profile" => f.Document with { DistributionProfile = f.Document.DistributionProfile with { Profile = "Staging" } },
            "platform" => f.Document with { Manifest = f.Document.Manifest with { Platform = "linux-x64" } },
            "model" => f.Document with { DistributionProfile = f.Document.DistributionProfile with { ModelBundleId = "another-model-bundle" } },
            _ => f.Document with { ArchiveSha256 = new string('b', 64) }
        };
        Assert.Throws<InvalidDataException>(() => DesktopPackageMetadataPolicy.ValidateDocument(document, f.Release));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("case-duplicate")]
    [InlineData("nested-duplicate")]
    [InlineData("unknown-field")]
    [InlineData("truncated")]
    [InlineData("too-large")]
    public void Untrusted_metadata_JSON_is_bounded_and_rejects_ambiguous_or_unknown_properties(string mutation)
    {
        var text = Encoding.UTF8.GetString(DesktopPackageMetadataPolicy.SerializeDocument(Fixture().Document));
        var bytes = mutation switch
        {
            "duplicate" => Encoding.UTF8.GetBytes(text.Insert(1, "\"schemaVersion\":1,")),
            "case-duplicate" => Encoding.UTF8.GetBytes(text.Insert(1, "\"SchemaVersion\":1,")),
            "nested-duplicate" => Encoding.UTF8.GetBytes(text.Replace("\"product\":", "\"product\":\"VideoMaker\",\"product\":", StringComparison.Ordinal)),
            "unknown-field" => Encoding.UTF8.GetBytes(text.Insert(1, "\"unknown\":true,")),
            "truncated" => Encoding.UTF8.GetBytes(text[..^1]),
            _ => new byte[4 * 1024 * 1024 + 1]
        };
        Assert.Throws<InvalidDataException>(() => DesktopPackageMetadataPolicy.DeserializeDocument(bytes));
    }

    [Fact]
    public void Metadata_reference_is_a_small_API_artifact_bound_to_this_release_and_Setup()
    {
        var f = Fixture();
        DesktopPackageMetadataPolicy.ValidateReference(f.Release, Api);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(f.Release, Json).Length < DesktopMaintenanceProtocol.MaximumFrameBytes);
        Assert.DoesNotContain("managedFiles", JsonSerializer.Serialize(f.Release, Json), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("external")]
    [InlineData("http")]
    [InlineData("query")]
    [InlineData("release-id")]
    [InlineData("bootstrapper-id")]
    [InlineData("path-hash")]
    [InlineData("archive-hash")]
    [InlineData("schema")]
    [InlineData("size")]
    [InlineData("hash")]
    public void A_metadata_reference_cannot_select_another_origin_or_package_identity(string mutation)
    {
        var f = Fixture();
        var reference = f.Release.PackageMetadata!;
        var changed = mutation switch
        {
            "external" => reference with { DownloadUrl = "https://outside.invalid/" + reference.DownloadUrl },
            "http" => reference with { DownloadUrl = "http://metadata.invalid/" + reference.DownloadUrl },
            "query" => reference with { DownloadUrl = reference.DownloadUrl + "?token=synthetic" },
            "release-id" => reference with { DownloadUrl = reference.DownloadUrl.Replace(f.Release.ReleaseId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal) },
            "bootstrapper-id" => reference with { DownloadUrl = reference.DownloadUrl.Replace(f.Release.SharedBootstrapper!.BootstrapperId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal) },
            "path-hash" => reference with { DownloadUrl = reference.DownloadUrl[..^64] + new string('c', 64) },
            "archive-hash" => reference with { ArchiveSha256 = new string('c', 64) },
            "schema" => reference with { SchemaVersion = 2 },
            "size" => reference with { SizeBytes = 4 * 1024 * 1024 + 1 },
            _ => reference with { Sha256 = new string('g', 64) }
        };
        Assert.Throws<InvalidDataException>(() => DesktopPackageMetadataPolicy.ValidateReference(f.Release with { PackageMetadata = changed }, Api));
    }

    [Theory]
    [InlineData("archive-hash")]
    [InlineData("archive-size")]
    [InlineData("payload-root")]
    [InlineData("entry-hash")]
    [InlineData("archive-content")]
    [InlineData("entry-content")]
    [InlineData("missing-file")]
    public async Task Modified_archive_or_descriptor_cannot_materialize_trusted_internal_metadata(string mutation)
    {
        var f = Fixture("publish/");
        var release = f.Release;
        var document = f.Document;
        if (mutation == "archive-hash") release = release with { Sha256 = new string('e', 64), PackageMetadata = release.PackageMetadata! with { ArchiveSha256 = new string('e', 64) } };
        if (mutation == "archive-size") release = release with { SizeBytes = release.SizeBytes + 1 };
        if (mutation == "payload-root") document = document with { PayloadRoot = "different/" };
        if (mutation == "entry-hash") document = document with { Files = [document.Files[0] with { Sha256 = new string('f', 64) }, .. document.Files.Skip(1)] };
        if (mutation == "missing-file") document = document with { Files = document.Files.Skip(1).ToArray() };
        if (mutation == "archive-content")
        {
            var bytes = f.Bytes.ToArray(); bytes[^1] ^= 1;
            File.WriteAllBytes(f.Archive, bytes);
        }
        if (mutation == "entry-content")
        {
            f.Payload["TOOL-LOCAL.exe"][^1] ^= 1;
            var bytes = Archive(f.Payload, "publish/");
            File.WriteAllBytes(f.Archive, bytes);
            document = document with { ArchiveSha256 = Hash(bytes) };
            release = BindDocument(release with { SizeBytes = bytes.LongLength, Sha256 = Hash(bytes) }, document);
        }
        if (mutation is "payload-root" or "entry-hash" or "missing-file") release = BindDocument(release, document);
        var originalArchive = File.ReadAllBytes(f.Archive);
        var stage = Path.Combine(_root, "rejected");
        await Assert.ThrowsAsync<InvalidDataException>(() => DesktopPackageMetadataPolicy.ExtractAndMaterializeAsync(f.Archive, stage, release, document, default));
        Assert.All(Generated, name => Assert.False(File.Exists(Path.Combine(stage, name))));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
        Assert.Equal(originalArchive, File.ReadAllBytes(f.Archive));
    }

    [Fact]
    public async Task Cancellation_does_not_create_completion_metadata()
    {
        var f = Fixture();
        var stage = Path.Combine(_root, "cancelled");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DesktopPackageMetadataPolicy.ExtractAndMaterializeAsync(f.Archive, stage, f.Release, f.Document, cancellation.Token));
        Assert.All(Generated, path => Assert.False(File.Exists(Path.Combine(stage, path))));
    }

    [Fact]
    public async Task Nonempty_destination_is_preserved_and_never_adopted_as_owned_staging()
    {
        var f = Fixture();
        var stage = Path.Combine(_root, "existing");
        Directory.CreateDirectory(stage);
        var sentinel = Path.Combine(stage, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "user file");
        await Assert.ThrowsAsync<InvalidDataException>(() => DesktopPackageMetadataPolicy.ExtractAndMaterializeAsync(f.Archive, stage, f.Release, f.Document, default));
        Assert.Equal("user file", await File.ReadAllTextAsync(sentinel));
        Assert.Single(Directory.GetFiles(stage));
    }

    private static DesktopPackageMetadataDocument Mutate(DesktopPackageMetadataDocument value, string mutation) => mutation switch
    {
        "archive-hash" => value with { ArchiveSha256 = "not-a-sha256" },
        "schema" => value with { SchemaVersion = 2 },
        "payload-traversal" => value with { PayloadRoot = "../" },
        "payload-two-levels" => value with { PayloadRoot = "outer/publish/" },
        "payload-no-slash" => value with { PayloadRoot = "publish" },
        "duplicate" => value with { Files = [.. value.Files, value.Files[0]] },
        "case-duplicate" => value with { Files = [.. value.Files, value.Files[0] with { Path = value.Files[0].Path.ToLowerInvariant() }] },
        "file-path" => value with { Files = [value.Files[0] with { Path = "../escape.txt" }, .. value.Files.Skip(1)] },
        "file-size" => value with { Files = [value.Files[0] with { SizeBytes = -1 }, .. value.Files.Skip(1)] },
        "file-hash" => value with { Files = [value.Files[0] with { Sha256 = new string('g', 64) }, .. value.Files.Skip(1)] },
        "generated-file" => value with { Files = [.. value.Files, new(Generated[0], 1, new string('0', 64))] },
        "unmanaged-file" => value with { Manifest = value.Manifest with { ManagedFiles = [.. value.Manifest.ManagedFiles, "uninspected.dll"] } },
        "missing-generated" => value with { Manifest = value.Manifest with { ManagedFiles = value.Manifest.ManagedFiles.Where(x => x != Generated[0]).ToArray() } },
        "expanded-size" => value with { ExpandedSizeBytes = value.ExpandedSizeBytes + 1 },
        "entry-count" => value with { EntryCount = 0 },
        "user-settings" => AddFile(value, "appsettings.user.json"),
        "setup-receipt" => AddFile(value, "static-setup-receipt.json"),
        "git-metadata" => AddFile(value, ".git/config"),
        "node-modules" => AddFile(value, "node_modules/package/index.js"),
        "profile-protocol" => value with { DistributionProfile = value.DistributionProfile with { DistributionProtocol = 2 } },
        "profile-model" => value with { DistributionProfile = value.DistributionProfile with { ModelBundleId = "" } },
        "minimum-bootstrapper" => value with { DistributionProfile = value.DistributionProfile with { MinimumBootstrapperVersion = "1.0.0" } },
        _ => throw new ArgumentOutOfRangeException(nameof(mutation))
    };

    private sealed record PackageFixture(string Archive, byte[] Bytes, Dictionary<string, byte[]> Payload,
        DesktopPackageMetadataDocument Document, DesktopReleaseResponse Release);

    private PackageFixture Fixture(string prefix = "")
    {
        Directory.CreateDirectory(_root);
        var payload = Payload();
        var bytes = Archive(payload, prefix);
        var archive = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(archive, bytes);
        var manifest = new DesktopUpdateManifest("VideoMaker", "1.2.0", 7, "win-x64", [.. payload.Keys, .. Generated]);
        var profile = new DesktopPackageDistributionProfile(1, 3, "Production", Api.AbsoluteUri, "Stable", "win-x64", ApprovedStaticModelBundle.Current.BundleId, "1.1.0");
        var document = new DesktopPackageMetadataDocument(1, Hash(bytes), prefix, manifest, profile,
            payload.Select(x => new DesktopPackageFileHash(x.Key, x.Value.LongLength, Hash(x.Value))).ToArray(), payload.Values.Sum(x => x.LongLength), payload.Count);
        var id = Guid.NewGuid();
        var setup = new DesktopSharedBootstrapperResponse(Guid.NewGuid(), "1.1.0", "Setup.exe", "/api/launcher-distribution/bootstrappers/test/download", 100,
            new string('a', 64), Api.AbsoluteUri, "Production", "Stable", "win-x64", 3, ApprovedStaticModelBundle.Current.BundleId);
        var release = new DesktopReleaseResponse(id, "VideoMaker", "1.2.0", 7, "Stable", "win-x64", null, null, DateTime.UtcNow,
            "raw.zip", DesktopArtifactDownloadPolicy.ApplicationPrefix + "raw.zip", bytes.LongLength, document.ArchiveSha256,
            DesktopArtifactSourceKinds.ExternalVps, DesktopInstallModes.StaticArchive, 3, null, profile.ModelBundleId, setup, profile.Profile, "1.1.0");
        return new(archive, bytes, payload, document, BindDocument(release, document));
    }

    private static DesktopPackageMetadataDocument AddFile(DesktopPackageMetadataDocument document, string path) => document with
    {
        Files = [.. document.Files, new(path, 1, new string('0', 64))],
        Manifest = document.Manifest with { ManagedFiles = [.. document.Manifest.ManagedFiles, path] },
        ExpandedSizeBytes = document.ExpandedSizeBytes + 1,
        EntryCount = document.EntryCount + 1
    };

    private static DesktopReleaseResponse BindDocument(DesktopReleaseResponse release, DesktopPackageMetadataDocument document)
    {
        // These fixtures model metadata already produced by trusted server inspection.
        // Malformed plans are encoded directly so ValidateDocument is what rejects them.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        var hash = Hash(bytes);
        var url = $"api/launcher-distribution/versions/{release.ReleaseId:D}/bootstrappers/{release.SharedBootstrapper!.BootstrapperId:D}/package-metadata/{hash}";
        return release with { PackageMetadata = new(1, url, bytes.Length, hash, document.ArchiveSha256) };
    }

    private static Dictionary<string, byte[]> Payload()
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["TOOL-LOCAL.exe"] = "MZ inert application fixture; never executed"u8.ToArray(),
            ["appsettings.json"] = "{\"Server\":{\"BaseUrl\":\"https://metadata.invalid/\"}}"u8.ToArray(),
            ["wwwroot/index.html"] = "<p>inert UI fixture</p>"u8.ToArray()
        };
        foreach (var path in DesktopMediaBundleIntegrity.RequiredRelativePaths) result[path] = Encoding.UTF8.GetBytes("inert fixture: " + path);
        return result;
    }

    private static byte[] Archive(Dictionary<string, byte[]> payload, string prefix,
        (string Path, byte[] Bytes, int? Attributes)[]? extras = null)
    {
        using var result = new MemoryStream();
        using (var zip = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in payload)
            {
                using var output = zip.CreateEntry(prefix + path, CompressionLevel.Fastest).Open();
                output.Write(bytes);
            }
            foreach (var (path, bytes, attributes) in extras ?? [])
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
                if (attributes is { } value) entry.ExternalAttributes = value;
                using var output = entry.Open(); output.Write(bytes);
            }
        }
        return result.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!_root.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test root.");
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
