using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution;
using TOOL_SHARED.Distribution.Updates;
using Xunit;

namespace TOOL_TESTS.Updates;

public sealed class DesktopReleaseArchiveInspectorTests
{
    private const string Url = "https://103.179.190.182:8443/apps/videomaker/1.2.0/VideoMaker.zip";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("")]
    [InlineData("publish/")]
    public async Task Plain_Publish_ZIP_generates_metadata_from_actual_files_without_editing_uploaded_bytes(string wrapper)
    {
        using var f = new Fixture();
        var zip = PlainPackage(wrapper);
        var result = await f.Inspect(zip);
        var metadata = Assert.IsType<DesktopPackageMetadataDocument>(result.PackageMetadata);
        Assert.Equal(wrapper, metadata.PayloadRoot);
        Assert.Equal("1.2.0", metadata.Manifest.Version); Assert.Equal(7, metadata.Manifest.BuildNumber);
        Assert.Equal("1.1.0", result.MinimumBootstrapperVersion);
        Assert.Equal(result.Sha256, metadata.ArchiveSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(), result.Sha256);
        Assert.Contains(metadata.Files, item => item.Path == "appsettings.json");
        Assert.DoesNotContain(metadata.Files, item => item.Path is "update-manifest.json" or "distribution-profile.json" or "package-inventory.json");
        Assert.Contains("update-manifest.json", metadata.Manifest.ManagedFiles);
        Assert.Contains("distribution-profile.json", metadata.Manifest.ManagedFiles);
        Assert.Contains("package-inventory.json", metadata.Manifest.ManagedFiles);
        Assert.Equal(metadata.Manifest.ManagedFiles.Order(StringComparer.Ordinal), metadata.Manifest.ManagedFiles);
        using var input = new MemoryStream(zip);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        foreach (var file in metadata.Files)
        {
            using var entry = archive.GetEntry(wrapper + file.Path)!.Open();
            Assert.Equal(Convert.ToHexString(SHA256.HashData(entry)).ToLowerInvariant(), file.Sha256);
        }
        DesktopPackageMetadataPolicy.ValidateDocument(metadata);
        Assert.Empty(Directory.GetDirectories(f.Scratch));
    }

    [Theory]
    [InlineData("update-manifest.json")]
    [InlineData("distribution-profile.json")]
    [InlineData("package-inventory.json")]
    public async Task Partial_metadata_is_not_silently_downgraded_to_plain_Publish(string remaining)
    {
        using var f = new Fixture();
        Assert.Equal("desktop_preparation_package_invalid", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(
            () => f.Inspect(PlainPackage(remainingMetadata: remaining)))).Code);
    }

    [Fact]
    public async Task Plain_Publish_version_is_read_from_native_binary_without_execution()
    {
        using var f = new Fixture();
        var error = await Assert.ThrowsAsync<DesktopReleaseInspectionException>(() => f.Inspect(PlainPackage(), expectedVersion: "1.3.0"));
        Assert.Equal("desktop_preparation_version_mismatch", error.Code);
        Assert.Contains("1.2.0 (build 7)", error.SafeMessage);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("mode")]
    [InlineData("enabled")]
    [InlineData("feature")]
    [InlineData("cache")]
    public async Task Plain_Publish_cannot_change_API_or_required_model_configuration(string fault)
    {
        using var f = new Fixture();
        Assert.Equal("desktop_preparation_profile_mismatch", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(
            () => f.Inspect(PlainPackage(settingsFault: fault)))).Code);
    }

    [Theory]
    [InlineData("duplicate-features")]
    [InlineData("case-duplicate-base-url")]
    public async Task Plain_Publish_rejects_duplicate_settings_instead_of_accepting_a_last_wins_or_case_ambiguous_configuration(string fault)
    {
        using var f = new Fixture();
        var error = await Assert.ThrowsAsync<DesktopReleaseInspectionException>(() => f.Inspect(PlainPackage(settingsFault: fault)));
        Assert.Equal("desktop_preparation_profile_mismatch", error.Code);
        Assert.Contains("trùng", error.SafeMessage);
        Assert.DoesNotContain("https://other.invalid/", error.SafeMessage);
        Assert.Empty(Directory.GetDirectories(f.Scratch));
    }

    [Theory]
    [InlineData("model.gguf")]
    [InlineData(".venv/python.exe")]
    [InlineData("nested/__pycache__/worker.pyc")]
    public async Task Plain_Publish_cannot_bundle_models_or_private_python_environment(string extra)
    {
        using var f = new Fixture();
        Assert.Equal("desktop_preparation_private_payload", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(
            () => f.Inspect(PlainPackage(extra: extra)))).Code);
    }

    [Fact]
    public async Task Existing_models_host_can_serve_application_ZIP_only_when_configured_and_compiled_prefixes_allow_it()
    {
        using var f = new Fixture();
        var result = await f.Inspect(PlainPackage(), url: "https://103.179.190.182:8443/models/publish-01.zip", allowModels: true);
        Assert.NotNull(result.PackageMetadata); Assert.Equal(1, f.Handler.Calls);
    }

    [Fact]
    public async Task Reads_actual_native_resources_and_entire_archive_then_computes_its_own_hash()
    {
        using var f = new Fixture();
        var zip = Package();
        var result = await f.Inspect(zip);
        Assert.Equal(zip.LongLength, result.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(), result.Sha256);
        Assert.Equal("VideoMaker.zip", result.FileName);
        Assert.Equal("1.2.0", result.PackageManifest.Version);
        Assert.Equal(7, result.PackageManifest.BuildNumber);
        Assert.Equal(DesktopDistributionProtocol.Shared, result.DistributionProfile.DistributionProtocol);
        Assert.Null(result.PackageMetadata);
        Assert.Empty(Directory.GetDirectories(f.Scratch));
        Assert.Equal(1, f.Handler.Calls);
        Assert.Null(f.Handler.Authorization);
        Assert.Null(f.Handler.Cookie);
    }

    [Fact]
    public async Task A_second_version_can_be_inspected_without_changing_bootstrapper_resource_fixture()
    {
        using var f = new Fixture();
        var bootstrapperPath = FixtureBinary("A");
        var before = SHA256.HashData(File.ReadAllBytes(bootstrapperPath));
        var binding = DesktopExecutableMetadata.ReadBootstrapperBinding(bootstrapperPath);
        Assert.Equal("Production", binding.Profile);
        Assert.Equal("https://metadata.invalid/", binding.ApiBaseUrl);
        Assert.Equal("1.2.0", binding.Version);
        var a = await f.Inspect(Package());
        var b = await f.Inspect(Package(version: "1.3.0", build: 1, binary: "B"), expectedVersion: "1.3.0");
        Assert.NotEqual(a.Sha256, b.Sha256);
        Assert.Equal("1.3.0", b.PackageManifest.Version);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(bootstrapperPath)));
        // This verifies PE resources/inspection, not installed model inference.
    }

    [Fact]
    public async Task PowerShell_UTF8_BOM_in_package_JSON_is_supported()
    {
        using var f = new Fixture();
        var result = await f.Inspect(Package(jsonBom: true));
        Assert.Equal("1.2.0", result.PackageManifest.Version);
    }

    [Theory]
    [InlineData("http://103.179.190.182:8443/apps/videomaker/a.zip")]
    [InlineData("https://103.179.190.182:8443/models/a.zip")]
    [InlineData("https://103.179.190.182/apps/videomaker/a.zip")]
    [InlineData("https://127.0.0.1/apps/videomaker/a.zip")]
    [InlineData("https://outside.invalid/apps/videomaker/a.zip")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/a.zip?token=hidden")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/%2e%2e/a.zip")]
    public async Task Unapproved_URL_is_rejected_before_HTTP(string url)
    {
        using var f = new Fixture();
        var error = await Assert.ThrowsAsync<DesktopReleaseInspectionException>(() => f.Inspect(Package(), url: url));
        Assert.Equal("desktop_preparation_url_invalid", error.Code);
        Assert.Equal(0, f.Handler.Calls);
    }

    [Theory]
    [InlineData("text/html", 200, "desktop_preparation_not_zip")]
    [InlineData("application/zip", 404, "desktop_preparation_download_failed")]
    [InlineData("application/zip", 302, "desktop_preparation_download_failed")]
    public async Task Invalid_HTTP_response_cannot_promote_a_package(string mime, int status, string code)
    {
        using var f = new Fixture(); f.Handler.Mime = mime; f.Handler.Status = (HttpStatusCode)status;
        var error = await Assert.ThrowsAsync<DesktopReleaseInspectionException>(() => f.Inspect(Package()));
        Assert.Equal(code, error.Code);
        Assert.Empty(Directory.GetDirectories(f.Scratch));
    }

    [Fact]
    public async Task Declared_size_does_not_replace_actual_download_size()
    {
        using var f = new Fixture(); f.Handler.DeclaredLength = 9;
        Assert.Equal("desktop_preparation_size_invalid", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(() => f.Inspect(Package()))).Code);
    }

    [Fact]
    public async Task Truncated_file_is_not_ready_and_scratch_is_cleaned()
    {
        using var f = new Fixture(); var zip = Package(); f.Handler.DeclaredLength = zip.Length + 1;
        Assert.Equal("desktop_preparation_download_incomplete", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(() => f.Inspect(zip))).Code);
        Assert.Empty(Directory.GetDirectories(f.Scratch));
    }

    [Fact]
    public async Task File_name_is_derived_safely_and_an_untrusted_header_cannot_choose_a_path()
    {
        using var f = new Fixture(); f.Handler.FileName = "../../untrusted.zip";
        var result = await f.Inspect(Package());
        Assert.StartsWith("VideoMaker-", result.FileName);
        Assert.DoesNotContain("/", result.FileName);
    }

    [Fact]
    public async Task Version_entered_on_form_must_match_manifest()
    {
        using var f = new Fixture();
        var error = await Assert.ThrowsAsync<DesktopReleaseInspectionException>(() => f.Inspect(Package(), expectedVersion: "1.4.0"));
        Assert.Equal("desktop_preparation_version_mismatch", error.Code);
        Assert.Contains("1.4.0", error.SafeMessage);
        Assert.Contains("1.2.0 (build 7)", error.SafeMessage);
    }

    [Fact]
    public async Task Native_binary_version_and_build_are_verified_without_execution()
    {
        using var f = new Fixture();
        Assert.Equal("desktop_preparation_binary_mismatch", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(
            () => f.Inspect(Package(build: 8)))).Code);
    }

    [Fact]
    public async Task Declared_profile_cannot_switch_API_or_model_policy()
    {
        using var f = new Fixture();
        Assert.Equal("desktop_preparation_profile_mismatch", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(
            () => f.Inspect(Package(profile: "OtherProfile")))).Code);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("C:/escape.txt")]
    [InlineData("/escape.txt")]
    [InlineData("nested\\escape.txt")]
    [InlineData("CON.txt")]
    [InlineData("nested/file:stream")]
    [InlineData("nested/file. ")]
    public async Task Unsafe_archive_entry_is_blocked(string entry)
    {
        using var f = new Fixture();
        Assert.Equal("desktop_preparation_package_invalid", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(
            () => f.Inspect(Package(extra: entry)))).Code);
        Assert.Empty(Directory.GetDirectories(f.Scratch));
    }

    [Fact]
    public async Task A_modified_archive_inventory_is_rejected()
    {
        using var f = new Fixture();
        Assert.Equal("desktop_preparation_package_invalid", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(
            () => f.Inspect(Package(extra: "unowned.txt")))).Code);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("size")]
    [InlineData("duplicate")]
    [InlineData("case-duplicate")]
    [InlineData("unowned")]
    [InlineData("excludes")]
    [InlineData("invalid-hash")]
    public async Task Package_inventory_is_required_and_must_match_actual_files(string fault)
    {
        using var f = new Fixture();
        Assert.Equal("desktop_preparation_package_invalid", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(
            () => f.Inspect(Package(inventoryFault: fault)))).Code);
        Assert.Empty(Directory.GetDirectories(f.Scratch));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("corrupt-file")]
    public async Task Every_inventory_hash_is_computed_from_full_entry_content(string fault)
    {
        using var f = new Fixture();
        Assert.Equal("desktop_preparation_inventory_mismatch", (await Assert.ThrowsAsync<DesktopReleaseInspectionException>(
            () => f.Inspect(Package(inventoryFault: fault)))).Code);
        Assert.Empty(Directory.GetDirectories(f.Scratch));
    }

    [Theory]
    [InlineData("127.0.0.1", false)] [InlineData("10.0.0.1", false)]
    [InlineData("192.168.1.1", false)] [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)] [InlineData("::1", false)]
    [InlineData("::ffff:127.0.0.1", false)] [InlineData("2001:db8::1", false)]
    [InlineData("103.179.190.182", true)] [InlineData("2606:4700:4700::1111", true)]
    public void Transport_rejects_non_public_destination_addresses(string ip, bool expected) =>
        Assert.Equal(expected, DesktopReleaseInspectionTransport.IsPublicAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("1.2.0", true)] [InlineData("1.10.0", true)] [InlineData("1.2", false)]
    [InlineData("v1.2.0", false)] [InlineData("01.2.0", false)] [InlineData("1.2.0-beta", false)]
    [InlineData("2147483648.0.0", false)] [InlineData("1.2.0\n", false)]
    public void Shared_version_input_is_strict_and_bounded(string value, bool expected) =>
        Assert.Equal(expected, DesktopReleaseVersionPolicy.IsSupportedStableVersion(value));

    private static string FixtureBinary(string variant) => Path.Combine(AppContext.BaseDirectory, "DistributionResources", $"DistributionResources{variant}.exe");

    private sealed record InventoryFile(string Path, long SizeBytes, string Sha256);

    private static byte[] PlainPackage(string wrapper = "", string? remainingMetadata = null, string? settingsFault = null, string? extra = null)
    {
        using var originalBytes = new MemoryStream(Package());
        using var original = new ZipArchive(originalBytes, ZipArchiveMode.Read);
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in original.Entries)
            {
                if ((entry.FullName is "update-manifest.json" or "distribution-profile.json" or "package-inventory.json")
                    && entry.FullName != remainingMetadata) continue;
                using var input = entry.Open(); using var output = zip.CreateEntry(wrapper + entry.FullName).Open(); input.CopyTo(output);
            }
            using (var settings = zip.CreateEntry(wrapper + "appsettings.json").Open())
            {
                var settingsJson = JsonSerializer.Serialize(new
                {
                    Server = new { BaseUrl = settingsFault == "api" ? "https://other.invalid/" : "https://metadata.invalid/" },
                    ModelDistribution = new { Mode = settingsFault == "mode" ? "Server" : "StaticArchive", Enabled = settingsFault == "enabled", CacheRoot = settingsFault == "cache" ? "D:/private/cache" : "" },
                    Features = new { VietsubEnabled = true, VietsubOcrEnabled = true, VietsubLocalTranslationEnabled = true, VietsubLocalVoiceEnabled = settingsFault != "feature" }
                });
                if (settingsFault == "duplicate-features")
                    settingsJson = settingsJson[..^1] + ",\"Features\":{\"VietsubEnabled\":true,\"VietsubOcrEnabled\":true,\"VietsubLocalTranslationEnabled\":true,\"VietsubLocalVoiceEnabled\":true}}";
                if (settingsFault == "case-duplicate-base-url")
                    settingsJson = settingsJson.Replace("\"BaseUrl\":\"https://metadata.invalid/\"", "\"BaseUrl\":\"https://metadata.invalid/\",\"baseurl\":\"https://other.invalid/\"", StringComparison.Ordinal);
                settings.Write(Encoding.UTF8.GetBytes(settingsJson));
            }
            if (extra is not null) { using var output = zip.CreateEntry(wrapper + extra).Open(); output.Write("private test payload"u8); }
        }
        return bytes.ToArray();
    }

    private static byte[] Package(string version = "1.2.0", int build = 7, string binary = "A", string profile = "Production", string? extra = null, bool jsonBom = false, string? inventoryFault = null)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["TOOL-LOCAL.exe"] = File.ReadAllBytes(FixtureBinary(binary)),
            ["tools/ffmpeg/ffmpeg.exe"] = "synthetic media fixture"u8.ToArray(),
            ["tools/ffmpeg/ffprobe.exe"] = "synthetic probe fixture"u8.ToArray(),
            ["tools/ffmpeg/LICENSE.txt"] = "Synthetic test license"u8.ToArray(),
            ["tools/ffmpeg/PROVENANCE.md"] = "- Approval scope: Release\n"u8.ToArray()
        };
        files["tools/ffmpeg/checksums.sha256"] = Encoding.UTF8.GetBytes(string.Join('\n',
            DesktopMediaBundleIntegrity.ChecksummedFileNames.Select(name => $"{Convert.ToHexString(SHA256.HashData(files[$"tools/ffmpeg/{name}"])).ToLowerInvariant()}  {name}")));
        files["distribution-profile.json"] = JsonSerializer.SerializeToUtf8Bytes(new DesktopPackageDistributionProfile(
            1, 3, profile, "https://metadata.invalid/", "Stable", "win-x64", "models-20261016-03", "1.0.0"), Json);
        var owned = files.Keys.Append("update-manifest.json").Append("package-inventory.json").ToArray();
        files["update-manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(new DesktopUpdateManifest("VideoMaker", version, build, "win-x64", owned), Json);
        if (jsonBom)
            foreach (var path in new[] { "update-manifest.json", "distribution-profile.json" })
                files[path] = new byte[] { 0xef, 0xbb, 0xbf }.Concat(files[path]).ToArray();
        var inventoryFiles = files.Select(x => new InventoryFile(x.Key, x.Value.LongLength, Convert.ToHexString(SHA256.HashData(x.Value)).ToLowerInvariant())).ToArray();
        switch (inventoryFault)
        {
            case "size": inventoryFiles[0] = inventoryFiles[0] with { SizeBytes = inventoryFiles[0].SizeBytes + 1 }; break;
            case "hash": inventoryFiles[0] = inventoryFiles[0] with { Sha256 = new string('0', 64) }; break;
            case "invalid-hash": inventoryFiles[0] = inventoryFiles[0] with { Sha256 = new string('g', 64) }; break;
            case "duplicate": inventoryFiles[^1] = inventoryFiles[0]; break;
            case "case-duplicate": inventoryFiles[^1] = inventoryFiles[0] with { Path = inventoryFiles[0].Path.ToLowerInvariant() }; break;
            case "unowned": inventoryFiles[^1] = inventoryFiles[^1] with { Path = "unknown.txt" }; break;
            case "corrupt-file": files["TOOL-LOCAL.exe"][^1] ^= 1; break;
        }
        if (inventoryFault != "missing")
        {
            files["package-inventory.json"] = JsonSerializer.SerializeToUtf8Bytes(new
            { schemaVersion = 1, excludes = new[] { inventoryFault == "excludes" ? "TOOL-LOCAL.exe" : "package-inventory.json" }, files = inventoryFiles }, Json);
            if (jsonBom) files["package-inventory.json"] = new byte[] { 0xef, 0xbb, 0xbf }.Concat(files["package-inventory.json"]).ToArray();
        }
        using var result = new MemoryStream();
        using (var zip = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in files) { using var stream = zip.CreateEntry(path).Open(); stream.Write(bytes); }
            if (extra is not null) { using var stream = zip.CreateEntry(extra).Open(); stream.Write("unowned"u8); }
        }
        return result.ToArray();
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "VM-Inspection-" + Guid.NewGuid().ToString("N")));
        public string Scratch => Path.Combine(Root, "scratch");
        public Handler Handler { get; } = new();
        private readonly HttpClient _client;
        public Fixture() { Directory.CreateDirectory(Root); _client = new HttpClient(Handler); }
        public Task<DesktopReleaseInspectionResult> Inspect(byte[] bytes, string expectedVersion = "1.2.0", string url = Url, bool allowModels = false)
        {
            Handler.Bytes = bytes;
            var inspector = new DesktopReleaseArchiveInspector(_client, new Environment(Root), Options.Create(new DesktopReleaseOptions
            {
                SharedBootstrapperEnabled = true, ApiBaseUrl = "https://metadata.invalid/", Profile = "Production",
                ScratchRoot = Scratch, AllowedExternalPrefixes = allowModels
                    ? [DesktopArtifactDownloadPolicy.ApplicationPrefix, DesktopArtifactDownloadPolicy.ModelHostPackagePrefix]
                    : [DesktopArtifactDownloadPolicy.ApplicationPrefix]
            }));
            return inspector.InspectAsync(new(Guid.NewGuid(), 1, url, expectedVersion), null, default);
        }
        public void Dispose()
        {
            _client.Dispose();
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Root.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test root.");
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public byte[] Bytes { get; set; } = [];
        public int Calls { get; private set; }
        public string Mime { get; set; } = "application/zip";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public long? DeclaredLength { get; set; }
        public string? FileName { get; set; }
        public string? Authorization { get; private set; }
        public string? Cookie { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Authorization = request.Headers.Authorization?.ToString();
            Cookie = request.Headers.TryGetValues("Cookie", out var values) ? string.Join(";", values) : null;
            var response = new HttpResponseMessage(Status) { Content = new ByteArrayContent(Bytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(Mime);
            if (DeclaredLength.HasValue) response.Content.Headers.ContentLength = DeclaredLength;
            if (FileName is not null) response.Content.Headers.ContentDisposition = new("attachment") { FileName = FileName };
            return Task.FromResult(response);
        }
    }

    private sealed class Environment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "InspectorFixture";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
