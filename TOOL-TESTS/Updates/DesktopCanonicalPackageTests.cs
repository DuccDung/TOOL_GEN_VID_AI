using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_TESTS.Updates;

// Real PowerShell ZIP I/O and native version-resource reads with inert fixtures.
// No application execution, remote access, SQL, model or Setup installation.
public sealed class DesktopCanonicalPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-canonical-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Streams_a_complete_flat_ZIP_and_binds_its_actual_hash_in_the_only_output_filename()
    {
        var source = Source();
        var nested = Path.Combine(source, "workers", "phụ đề.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        await File.WriteAllTextAsync(nested, "Nội dung fixture");
        foreach (var metadata in new[] { "update-manifest.json", "distribution-profile.json", "package-inventory.json" })
            await File.WriteAllTextAsync(Path.Combine(source, metadata), "stale metadata is excluded");
        var result = await Run("New-DesktopCanonicalPackage -PublishRoot $Source -Version '1.2.0' -BuildNumber 7 -OutputRoot $Output | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using var report = JsonDocument.Parse(result.Output);
        var path = report.RootElement.GetProperty("PackagePath").GetString()!;
        var bytes = await File.ReadAllBytesAsync(path);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal($"VideoMaker-1.2.0-7-win-x64-{hash}.zip", Path.GetFileName(path));
        Assert.Equal(hash, report.RootElement.GetProperty("Sha256").GetString());
        Assert.Equal(bytes.LongLength, report.RootElement.GetProperty("SizeBytes").GetInt64());
        Assert.False(report.RootElement.GetProperty("ServerInspected").GetBoolean());
        Assert.False(report.RootElement.GetProperty("InstalledModelsProbed").GetBoolean());
        Assert.False(report.RootElement.GetProperty("ProductionQualified").GetBoolean());
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "output"), "*", SearchOption.AllDirectories));
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(3, zip.Entries.Count);
        Assert.NotNull(zip.GetEntry("TOOL-LOCAL.exe"));
        Assert.NotNull(zip.GetEntry("workers/phụ đề.txt"));
        Assert.DoesNotContain(zip.Entries, x => x.FullName.Contains('\\'));
        Assert.DoesNotContain(zip.Entries, x => x.FullName.EndsWith("-manifest.json") || x.FullName == "package-inventory.json" || x.FullName == "distribution-profile.json");
        using var reader = new StreamReader(zip.GetEntry("workers/phụ đề.txt")!.Open());
        Assert.Equal("Nội dung fixture", await reader.ReadToEndAsync());
        Assert.Equal("stale metadata is excluded", await File.ReadAllTextAsync(Path.Combine(source, "update-manifest.json")));
    }

    [Fact]
    public async Task Repeated_packaging_reuses_identical_bytes_and_a_changed_payload_preserves_both_archives()
    {
        var source = Source();
        var result = await Run("""
            $a = New-DesktopCanonicalPackage $Source '1.2.0' 7 $Output
            $b = New-DesktopCanonicalPackage $Source '1.2.0' 7 $Output
            [IO.File]::WriteAllText((Join-Path $Source 'appsettings.json'), '{"fixture":"second"}')
            $c = New-DesktopCanonicalPackage $Source '1.2.0' 7 $Output
            @($a,$b,$c) | ConvertTo-Json -Compress
            """);
        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using var report = JsonDocument.Parse(result.Output);
        var rows = report.RootElement.EnumerateArray().ToArray();
        Assert.Equal(rows[0].GetProperty("PackagePath").GetString(), rows[1].GetProperty("PackagePath").GetString());
        Assert.True(rows[1].GetProperty("Reused").GetBoolean());
        Assert.NotEqual(rows[0].GetProperty("Sha256").GetString(), rows[2].GetProperty("Sha256").GetString());
        Assert.All(rows, row => Assert.True(File.Exists(row.GetProperty("PackagePath").GetString())));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, "output"), "*.zip", SearchOption.AllDirectories).Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "output"), "*.part", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(source, "TOOL-LOCAL.exe")));
    }

    [Fact]
    public async Task The_SDK_file_list_excludes_stale_payload_without_copying_a_staging_tree()
    {
        var source = Source();
        await File.WriteAllTextAsync(Path.Combine(source, "obsolete.dll"), "old publish");
        await File.WriteAllTextAsync(Path.Combine(_root, "files.txt"), "TOOL-LOCAL.exe\nappsettings.json\n");
        var result = await Run("New-DesktopCanonicalPackage $Source '1.2.0' 7 $Output -PublishFileListPath (Join-Path (Split-Path $Source -Parent) 'files.txt') | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using var report = JsonDocument.Parse(result.Output);
        using var zip = ZipFile.OpenRead(report.RootElement.GetProperty("PackagePath").GetString()!);
        Assert.Equal(2, zip.Entries.Count);
        Assert.Null(zip.GetEntry("obsolete.dll"));
        Assert.Equal("old publish", await File.ReadAllTextAsync(Path.Combine(source, "obsolete.dll")));
        Assert.Single(Directory.GetDirectories(Path.Combine(_root, "output")));
    }

    [Theory]
    [InlineData("appsettings.user.json")]
    [InlineData("cache/state.txt")]
    [InlineData(".ready.json")]
    [InlineData("static-setup-receipt.json")]
    [InlineData("private.key")]
    [InlineData("models/qwen.gguf")]
    public async Task Private_state_and_models_fail_before_a_new_archive_is_promoted(string file)
    {
        var source = Source();
        var path = Path.Combine(source, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "private fixture");
        var result = await Run("New-DesktopCanonicalPackage $Source '1.2.0' 7 $Output");
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        Assert.Equal("private fixture", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("1.3.0", 7)]
    [InlineData("1.2.0", 8)]
    public async Task Filename_identity_must_match_actual_native_version_resources(string version, int build)
    {
        Source();
        var result = await Run($"New-DesktopCanonicalPackage $Source '{version}' {build} $Output");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("version/build", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
    }

    [Fact]
    public async Task A_corrupt_existing_hash_named_archive_is_never_overwritten_or_deleted()
    {
        Source();
        var result = await Run("""
            $a = New-DesktopCanonicalPackage $Source '1.2.0' 7 $Output
            [IO.File]::WriteAllText($a.PackagePath, 'preserve corrupt artifact for review')
            New-DesktopCanonicalPackage $Source '1.2.0' 7 $Output
            """);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("immutable filename", result.Error, StringComparison.OrdinalIgnoreCase);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_root, "output"), "*.zip", SearchOption.AllDirectories));
        Assert.Equal("preserve corrupt artifact for review", await File.ReadAllTextAsync(file));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "output"), "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Output_inside_Publish_is_rejected_before_allocating_any_owned_archive()
    {
        var source = Source();
        var result = await Run("New-DesktopCanonicalPackage $Source '1.2.0' 7 (Join-Path $Source 'output')");
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(source, "output")));
    }

    private string Source()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "DistributionResources", "DistributionResourcesA.exe"), Path.Combine(source, "TOOL-LOCAL.exe"));
        File.WriteAllText(Path.Combine(source, "appsettings.json"), "{\"fixture\":true}");
        return source;
    }

    private async Task<(int ExitCode, string Output, string Error)> Run(string body)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var script = Path.Combine(_root, "run.ps1");
        await File.WriteAllTextAsync(script, """
            param([string]$Module,[string]$Source,[string]$Output)
            $ErrorActionPreference='Stop'
            Import-Module $Module -Force -DisableNameChecking
            """ + Environment.NewLine + body, new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Module",
            Path.Combine(repository.FullName, "scripts", "DesktopCanonicalPackage.psm1"), "-Source", Path.Combine(_root, "source"), "-Output", Path.Combine(_root, "output") }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        return (process.ExitCode, (await output).Trim(), await error);
    }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!_root.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test root.");
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
