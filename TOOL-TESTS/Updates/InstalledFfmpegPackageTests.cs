using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_TESTS.Updates;

// Controlled malformed ZIPs only. No model, Setup, SQL, media encoder or network
// runs here; a real final ZIP has a separate opt-in local qualification script.
public sealed class InstalledFfmpegPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-ffmpeg-zip-" + Guid.NewGuid().ToString("N"));
    private string Work => Path.Combine(_root, "qa output & phụ đề");

    [Fact]
    public async Task Wrong_package_hash_is_rejected_before_creating_the_work_directory()
    {
        var zip = CreatePackage();
        var result = await RunAsync(zip, new string('0', 64));
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SHA-256", result.Error);
        Assert.False(Directory.Exists(Work));
    }

    [Fact]
    public async Task Existing_work_directory_is_preserved_and_never_reused()
    {
        var zip = CreatePackage();
        Directory.CreateDirectory(Work);
        var customerFile = Path.Combine(Work, "keep.txt");
        File.WriteAllText(customerFile, "customer data");
        var result = await RunAsync(zip);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("new, absent WorkRoot", result.Error);
        Assert.Equal("customer data", File.ReadAllText(customerFile));
        Assert.Single(Directory.GetFiles(Work));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("folder/../../outside.txt")]
    [InlineData("folder\\outside.txt")]
    public async Task Unsafe_archive_names_fail_before_any_extraction(string name)
    {
        var zip = CreateRawZip([(name, "untrusted", 0)]);
        var result = await RunAsync(zip);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unsafe package entry", result.Error);
        Assert.False(Directory.Exists(Work));
        Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
    }

    [Fact]
    public async Task Link_archive_entry_is_rejected_before_creating_the_work_directory()
    {
        var zip = CreateRawZip([("linked.exe", "target.exe", unchecked((int)0xA0000000))]);
        var result = await RunAsync(zip);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("link or reparse", result.Error);
        Assert.False(Directory.Exists(Work));
    }

    [Fact]
    public async Task Inventory_corruption_is_rejected_before_executing_any_package_file()
    {
        var zip = CreatePackage(tamper: true);
        var result = await RunAsync(zip);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("inventory size/SHA-256 mismatch", result.Error);
        AssertFailureReport("inventory", inventoryVerified: false);
        Assert.False(Directory.Exists(Path.Combine(Work, "media-smoke")));
    }

    [Fact]
    public async Task Incompatible_protocol_is_rejected_after_hash_check_before_native_execution()
    {
        var zip = CreatePackage(protocol: 2);
        var result = await RunAsync(zip);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("compatible SharedVps", result.Error);
        AssertFailureReport("release-identity", inventoryVerified: true);
    }

    [Fact]
    public async Task Incomplete_owned_file_manifest_is_rejected_before_native_execution()
    {
        var zip = CreatePackage(omitManagedFile: true);
        var result = await RunAsync(zip);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("own exactly", result.Error);
        AssertFailureReport("release-identity", inventoryVerified: true);
    }

    [Fact]
    public async Task Non_native_executable_is_rejected_without_claiming_setup_or_model_completion()
    {
        var zip = CreatePackage();
        var result = await RunAsync(zip);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not a Windows executable", result.Error);
        AssertFailureReport("release-identity", inventoryVerified: true);
    }

    private void AssertFailureReport(string phase, bool inventoryVerified)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Work, "ffmpeg-package-verification.json")));
        var report = document.RootElement;
        Assert.Equal("Failed", report.GetProperty("status").GetString());
        Assert.Equal(phase, report.GetProperty("phase").GetString());
        Assert.True(report.GetProperty("applicationPayloadExtracted").GetBoolean());
        Assert.Equal(inventoryVerified, report.GetProperty("packageInventoryVerified").GetBoolean());
        foreach (var name in new[] { "setupCompleted", "installedModelInferenceChecked", "cleanWindowsChecked", "serverAccessChecked", "productionQualified" })
            Assert.False(report.GetProperty(name).GetBoolean());
    }

    private string CreatePackage(bool tamper = false, int protocol = 3, bool omitManagedFile = false)
    {
        var names = new[] { "TOOL-LOCAL.exe", "update-manifest.json", "distribution-profile.json", "package-inventory.json" };
        var files = new Dictionary<string, string>
        {
            ["TOOL-LOCAL.exe"] = "Controlled text fixture: must never execute.",
            ["update-manifest.json"] = JsonSerializer.Serialize(new
            {
                product = "VideoMaker", version = "1.2.0", buildNumber = 7, platform = "win-x64",
                managedFiles = omitManagedFile ? names.Skip(1).ToArray() : names
            }),
            ["distribution-profile.json"] = JsonSerializer.Serialize(new
            {
                schemaVersion = 1, distributionProtocol = protocol, profile = "Production", channel = "Stable", platform = "win-x64",
                modelBundleId = "models-20261016-03", minimumBootstrapperVersion = "1.0.0", serverBaseUrl = "https://fixture.invalid/"
            })
        };
        files["package-inventory.json"] = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, excludes = new[] { "package-inventory.json" },
            files = files.Select(file => new
            {
                path = file.Key, sizeBytes = Encoding.UTF8.GetByteCount(file.Value), sha256 = Hash(Encoding.UTF8.GetBytes(file.Value))
            }).ToArray()
        });
        if (tamper) files["TOOL-LOCAL.exe"] += "tampered";
        return CreateRawZip(files.Select(file => (file.Key, file.Value, 0)).ToArray());
    }

    private string CreateRawZip((string Name, string Content, int Attributes)[] entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "application.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Name);
            entry.ExternalAttributes = item.Attributes;
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(item.Content);
        }
        return path;
    }

    private async Task<ProcessResult> RunAsync(string zip, string? hash = null)
    {
        var script = Path.Combine(FindRepository(), "scripts", "Test-InstalledFfmpegPackage.ps1");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-PackagePath", zip, "-ExpectedSha256", hash ?? Hash(File.ReadAllBytes(zip)), "-WorkRoot", Work })
            start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return new(process.ExitCode, await stdout, await stderr);
    }

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    private static string FindRepository()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "scripts", "Test-InstalledFfmpegPackage.ps1"))) return current.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
