using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_TESTS.Updates;

// Pure release-file fixtures. No dotnet publish, real Setup execution or remote registration.
public sealed class ExternalDesktopReleasePipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-external-release-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Registration_binds_final_zip_setup_and_manifest_without_claiming_remote_verification()
    {
        var fixture = WriteFixture();
        var result = await RunAsync(fixture, "valid");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.Output));
        var value = document.RootElement;
        Assert.Equal("ExternalVps", JsonDocument.Parse(result.Output).RootElement.GetProperty("source").GetString());
        Assert.Equal(Hash(fixture.Package), value.GetProperty("sha256").GetString());
        Assert.Equal(Hash(fixture.Setup), value.GetProperty("setup").GetProperty("sha256").GetString());
        Assert.Equal("StaticArchive", value.GetProperty("installMode").GetString());
        Assert.Equal(2, value.GetProperty("distributionProtocol").GetInt32());
        Assert.Equal(37, value.GetProperty("packageManifest").GetProperty("buildNumber").GetInt32());
        Assert.False(value.TryGetProperty("verified", out _));
        Assert.False(value.TryGetProperty("productionQualified", out _));
        using var zip = ZipFile.OpenRead(fixture.Package);
        Assert.Null(zip.GetEntry("external-registration.json"));
    }

    [Theory]
    [InlineData("https://103.179.190.182/apps/videomaker/app.zip")]
    [InlineData("http://103.179.190.182:8443/apps/videomaker/app.zip")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker-evil/app.zip")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/../app.zip")]
    [InlineData("https://103.179.190.182:8443/models/%2e%2e/app.zip")]
    [InlineData("https://103.179.190.182:8443/models/app.zip?token=synthetic")]
    [InlineData("https://103.179.190.182:8443/models/app.zip#fragment")]
    [InlineData("https://user@103.179.190.182:8443/models/app.zip")]
    [InlineData("https://unapproved.example.test:8443/models/app.zip")]
    [InlineData("https://103.179.190.182:8443/models/app name.zip")]
    public async Task Unapproved_url_is_rejected_before_reading_missing_package(string url)
    {
        Directory.CreateDirectory(_root);
        var fixture = new Fixture(Path.Combine(_root, "missing.zip"), Path.Combine(_root, "missing.exe"), "", Path.Combine(_root, "output.json"));
        var result = await RunAsync(fixture, "url", url);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("does not exist", result.Error);
        Assert.False(File.Exists(fixture.Output));
    }

    [Theory]
    [InlineData("setup-version")]
    [InlineData("setup-build")]
    [InlineData("setup-channel")]
    [InlineData("setup-api")]
    [InlineData("setup-package-hash")]
    [InlineData("package-byte-changed")]
    [InlineData("setup-byte-changed")]
    [InlineData("loopback-api")]
    public async Task Wrong_binding_or_changed_bytes_emit_no_registration(string mutation)
    {
        var fixture = WriteFixture();
        var result = await RunAsync(fixture, mutation);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(fixture.Output));
    }

    [Theory]
    [InlineData("traversal")]
    [InlineData("duplicate")]
    [InlineData("link")]
    [InlineData("manifest-inventory")]
    public async Task Unsafe_zip_is_rejected_by_registration_validator(string mutation)
    {
        var fixture = WriteFixture(mutation);
        var result = await RunAsync(fixture, "valid");
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public async Task Existing_registration_is_never_overwritten()
    {
        var fixture = WriteFixture();
        await File.WriteAllTextAsync(fixture.Output, "preserve-existing-handover");
        var result = await RunAsync(fixture, "valid");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("preserve-existing-handover", await File.ReadAllTextAsync(fixture.Output));
    }

    [Fact]
    public async Task Oversized_application_filename_is_rejected_before_reading_zip()
    {
        Directory.CreateDirectory(_root);
        var fixture = new Fixture(Path.Combine(_root, new string('a', 177) + ".zip"), Path.Combine(_root, "missing.exe"), "", Path.Combine(_root, "output.json"));
        var result = await RunAsync(fixture, "filename");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("filename exceeds the 180", result.Error);
        Assert.DoesNotContain("does not exist", result.Error);
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public async Task Serialized_registration_over_four_MiB_is_rejected_before_creating_output()
    {
        var fixture = WriteFixture("registration-json-size");
        var result = await RunAsync(fixture, "valid");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Serialized external registration exceeds the 4 MiB", result.Error);
        Assert.False(File.Exists(fixture.Output));
    }

    private Fixture WriteFixture(string? mutation = null)
    {
        Directory.CreateDirectory(_root);
        var package = Path.Combine(_root, "VideoMaker-1.2.0-37-win-x64.zip");
        var setup = Path.Combine(_root, "VideoMaker Setup.exe");
        File.WriteAllBytes(setup, "MZ synthetic local metadata fixture"u8.ToArray());
        var files = new[] { "TOOL-LOCAL.exe", "package-inventory.json", "update-manifest.json" };
        var manifest = JsonSerializer.Serialize(new { product = "VideoMaker", version = "1.2.0", buildNumber = 37,
            platform = "win-x64", managedFiles = mutation == "manifest-inventory" ? ["missing.exe", .. files.Skip(1)] : files });
        if (mutation == "registration-json-size")
        {
            // Still below the ZIP manifest bound; the complete indented request plus
            // package/Setup metadata crosses the HTTP registration bound afterwards.
            manifest = manifest[..^1] + ",\"padding\":\"" + new string('x', 4_194_000) + "\"}";
            Assert.InRange(Encoding.UTF8.GetByteCount(manifest), 1, 4 * 1024 * 1024);
        }
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            foreach (var name in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(name == "update-manifest.json" ? manifest : "synthetic");
            }
            if (mutation == "traversal") zip.CreateEntry("../outside.txt");
            if (mutation == "duplicate") zip.CreateEntry("tool-local.EXE");
            if (mutation == "link") zip.CreateEntry("link.txt").ExternalAttributes = unchecked((int)0xA1FF0000);
        }
        var binding = Path.Combine(_root, "binding.json");
        File.WriteAllText(binding, JsonSerializer.Serialize(new { schemaVersion = 1, version = "1.2.0", buildNumber = 37,
            packageSha256 = Hash(package), channel = "Beta", platform = "win-x64", serverBaseUrl = "https://api.example.test/",
            installMode = "StaticArchive", distributionProtocol = 2, modelBundleId = "models-20261016-03" }));
        return new Fixture(package, setup, binding, Path.Combine(_root, "external-registration.json"));
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(Fixture fixture, string mutation, string? url = null)
    {
        var runner = Path.Combine(_root, "validate-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(runner, """
            param($Module,$Package,$Setup,$BindingPath,$Destination,$Mutation,$DownloadUrl)
            $ErrorActionPreference='Stop'
            [Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
            Import-Module $Module -Force
            if ($Mutation -eq 'url') { Test-DesktopExternalDownloadUrl $DownloadUrl; exit 0 }
            if ($Mutation -eq 'filename') { Get-DesktopRegistrationPackage -PackagePath $Package -Version '1.2.0' -BuildNumber 37; exit 0 }
            $binding=Get-Content -LiteralPath $BindingPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $expectedPackage=(Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant()
            $expectedSetup=(Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash.ToLowerInvariant()
            $api='https://api.example.test/'
            switch ($Mutation) {
                'setup-version' { $binding.version='9.9.9' }
                'setup-build' { $binding.buildNumber=38 }
                'setup-channel' { $binding.channel='Stable' }
                'setup-api' { $binding.serverBaseUrl='https://other.example.test/' }
                'setup-package-hash' { $binding.packageSha256=('0'*64) }
                'loopback-api' { $api='https://localhost:7202/' }
                'package-byte-changed' { [IO.File]::AppendAllText($Package,'changed') }
                'setup-byte-changed' { [IO.File]::AppendAllText($Setup,'changed') }
            }
            $null=New-DesktopExternalRegistration -PackagePath $Package -SetupPath $Setup -Version '1.2.0' -BuildNumber 37 -Channel Beta `
                -ServerBaseUrl $api -DownloadUrl 'https://103.179.190.182:8443/apps/videomaker/1.2.0-37/app.zip' `
                -SetupBinding $binding -OutputPath $Destination -ExpectedPackageSha256 $expectedPackage -ExpectedSetupSha256 $expectedSetup
            @{source='ExternalVps'} | ConvertTo-Json -Compress
            """, new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner,
            "-Module", Path.Combine(FindRepository(), "scripts", "DesktopExternalRelease.psm1"), "-Package", fixture.Package,
            "-Setup", fixture.Setup, "-BindingPath", fixture.Binding, "-Destination", fixture.Output, "-Mutation", mutation,
            "-DownloadUrl", url ?? "none" }) start.ArgumentList.Add(value);
        var unicodeTemp = Path.Combine(_root, "TEMP có dấu"); Directory.CreateDirectory(unicodeTemp);
        start.Environment["TEMP"] = unicodeTemp; start.Environment["TMP"] = unicodeTemp;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string FindRepository()
    {
        for (var item = new DirectoryInfo(AppContext.BaseDirectory); item is not null; item = item.Parent)
            if (File.Exists(Path.Combine(item.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return item.FullName;
        throw new InvalidOperationException("Source checkout required.");
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed record Fixture(string Package, string Setup, string Binding, string Output);
}
