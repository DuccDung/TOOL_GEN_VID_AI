using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_TESTS.Updates;

// Controlled file fixtures only. These tests never publish or execute a desktop,
// download models, access SQL/VPS or qualify a customer package.
public sealed class DesktopPublishedPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-published-package-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Package_metadata_is_at_zip_root_and_covers_every_final_file()
    {
        var source = CreateSource();
        Directory.CreateDirectory(Path.Combine(source, "unicode"));
        File.WriteAllText(Path.Combine(source, "unicode", "phụ đề.txt"), "fixture subtitle");
        File.WriteAllText(Path.Combine(source, ".hidden"), "hidden fixture");
        File.SetAttributes(Path.Combine(source, ".hidden"), FileAttributes.Hidden);
        var result = await RunAsync("""
            Copy-DesktopPublishedPayload $Source $Package | Out-Null
            New-DesktopSharedPackageMetadata $Package '1.2.0' 7 'https://fixture.invalid/'
            New-DesktopPackageZip $Package $Zip
            Expand-DesktopPackageZip $Zip $Extracted | Out-Null
            Test-DesktopPackageInventory $Extracted | ConvertTo-Json -Compress
            """);
        Assert.True(result.ExitCode == 0, result.Error + Environment.NewLine + result.Output);
        using var archive = ZipFile.OpenRead(Path.Combine(_root, "package.zip"));
        Assert.NotNull(archive.GetEntry("TOOL-LOCAL.exe"));
        Assert.Null(archive.GetEntry("source/TOOL-LOCAL.exe"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "extracted", "update-manifest.json")));
        var managed = manifest.RootElement.GetProperty("managedFiles").EnumerateArray().Select(x => x.GetString()).Order().ToArray();
        Assert.Equal(archive.Entries.Select(x => x.FullName).Order(), managed);
        using var profile = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "extracted", "distribution-profile.json")));
        Assert.Equal(3, profile.RootElement.GetProperty("distributionProtocol").GetInt32());
        Assert.Equal("models-20261016-03", profile.RootElement.GetProperty("modelBundleId").GetString());
        Assert.DoesNotContain(archive.Entries, x => x.Name.EndsWith(".pdb") || x.Name.EndsWith(".xml"));
        using var report = JsonDocument.Parse(result.Output.Trim());
        Assert.True(report.RootElement.GetProperty("InventoryVerified").GetBoolean());
    }

    [Fact]
    public async Task Resolved_publish_file_list_excludes_stale_files_in_a_reused_folder()
    {
        var source = CreateSource();
        File.WriteAllText(Path.Combine(source, "obsolete.dll"), "stale earlier publish");
        File.WriteAllLines(Path.Combine(_root, "files.txt"), ["TOOL-LOCAL.exe", "appsettings.json"]);
        var result = await RunAsync("Copy-DesktopPublishedPayload $Source $Package -PublishFileListPath $List | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Error + Environment.NewLine + result.Output);
        Assert.False(File.Exists(Path.Combine(_root, "package", "obsolete.dll")));
        Assert.True(File.Exists(Path.Combine(_root, "package", "TOOL-LOCAL.exe")));
        Assert.Equal("stale earlier publish", File.ReadAllText(Path.Combine(source, "obsolete.dll")));
    }

    [Fact]
    public async Task Junction_is_rejected_before_copying_or_reading_its_target()
    {
        var source = CreateSource();
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "customer fixture preserved");
        var result = await RunAsync("""
            $outside = Join-Path (Split-Path -Parent $Source) 'outside'
            New-Item -ItemType Junction -Path (Join-Path $Source 'linked') -Target $outside | Out-Null
            Copy-DesktopPublishedPayload $Source $Package
            """);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("reparse", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(_root, "package")));
        Assert.Equal("customer fixture preserved", File.ReadAllText(Path.Combine(outside, "keep.txt")));
        Directory.Delete(Path.Combine(source, "linked"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../outside.txt")]
    [InlineData("nested\\..\\file.txt")]
    [InlineData("TOOL-LOCAL.exe\nTOOL-local.exe")]
    [InlineData("missing.dll")]
    public async Task Invalid_resolved_publish_list_fails_before_copying_any_payload(string list)
    {
        CreateSource();
        File.WriteAllText(Path.Combine(_root, "files.txt"), list);
        var result = await RunAsync("Copy-DesktopPublishedPayload $Source $Package -PublishFileListPath $List");
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_root, "package")));
    }

    [Theory]
    [InlineData("Qwen.gguf")]
    [InlineData("model.safetensors")]
    [InlineData("piper-offline.zip")]
    [InlineData(".ready.json")]
    [InlineData(".ready-voice.json")]
    [InlineData("static-setup-receipt.json")]
    [InlineData("static-model-install.json")]
    [InlineData("static-model-install.pending.json")]
    [InlineData("provider-secrets.bin")]
    [InlineData("provider-secrets.bin.tmp")]
    [InlineData("signing.pfx")]
    [InlineData("private.pem")]
    [InlineData("private.key")]
    [InlineData(".env.production")]
    [InlineData("appsettings.Production.json")]
    [InlineData("_translation_worker/appsettings.json")]
    [InlineData("auth-token.bin")]
    [InlineData("cache/stale.txt")]
    [InlineData(".venv/pyvenv.cfg")]
    public async Task Model_payloads_machine_state_and_runtime_cache_never_enter_an_app_zip(string name)
    {
        var source = CreateSource();
        var forbidden = Path.Combine(source, name);
        Directory.CreateDirectory(Path.GetDirectoryName(forbidden)!);
        File.WriteAllText(forbidden, "controlled forbidden fixture");
        var result = await RunAsync("Copy-DesktopPublishedPayload $Source $Package");
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_root, "package")));
        Assert.Equal("controlled forbidden fixture", File.ReadAllText(forbidden));
    }

    [Fact]
    public async Task Promotion_is_complete_and_repeat_publish_reuses_identical_payload_without_replacing_zip()
    {
        Directory.CreateDirectory(_root);
        var first = CreateStage("same fixture", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var second = CreateStage("same fixture", new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var original = File.ReadAllBytes(Path.Combine(first, "VideoMaker-1.2.0-7-win-x64.zip"));
        Assert.NotEqual(Hash(Path.Combine(first, "VideoMaker-1.2.0-7-win-x64.zip")), Hash(Path.Combine(second, "VideoMaker-1.2.0-7-win-x64.zip")));
        var result = await RunAsync("""
            Complete-DesktopPublishedPackage $First $Output '1.2.0' 7 | Out-Null
            Complete-DesktopPublishedPackage $Second $Output '1.2.0' 7 | ConvertTo-Json -Compress
            """, first, second);
        Assert.True(result.ExitCode == 0, result.Error + Environment.NewLine + result.Output);
        using var report = JsonDocument.Parse(result.Output.Trim());
        Assert.True(report.RootElement.GetProperty("ReusedExisting").GetBoolean());
        Assert.False(report.RootElement.GetProperty("SetupBuilt").GetBoolean());
        Assert.False(report.RootElement.GetProperty("ProductionQualified").GetBoolean());
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(_root, "output", "1.2.0-7", "VideoMaker-1.2.0-7-win-x64.zip")));
        Assert.False(Directory.Exists(first));
    }

    [Fact]
    public async Task Changed_same_version_build_is_rejected_and_existing_release_is_preserved()
    {
        Directory.CreateDirectory(_root);
        var first = CreateStage("old fixture");
        var second = CreateStage("new fixture");
        var before = File.ReadAllBytes(Path.Combine(first, "VideoMaker-1.2.0-7-win-x64.zip"));
        var result = await RunAsync("""
            Complete-DesktopPublishedPackage $First $Output '1.2.0' 7 | Out-Null
            Complete-DesktopPublishedPackage $Second $Output '1.2.0' 7
            """, first, second);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Increase the build number", result.Error);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_root, "output", "1.2.0-7", "VideoMaker-1.2.0-7-win-x64.zip")));
        Assert.True(Directory.Exists(second));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("report")]
    [InlineData("missing-report")]
    [InlineData("extra")]
    public async Task Incomplete_or_tampered_staging_never_becomes_a_release(string mutation)
    {
        Directory.CreateDirectory(_root);
        var first = CreateStage("fixture");
        if (mutation == "hash") File.AppendAllText(Path.Combine(first, "VideoMaker-1.2.0-7-win-x64.zip"), "tampered");
        if (mutation == "report") File.WriteAllText(Path.Combine(first, "package-report.json"), "{}");
        if (mutation == "missing-report") File.Delete(Path.Combine(first, "package-report.json"));
        if (mutation == "extra") File.WriteAllText(Path.Combine(first, "stale.zip"), "unexpected");
        var result = await RunAsync("Complete-DesktopPublishedPackage $First $Output '1.2.0' 7", first);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_root, "output", "1.2.0-7")));
        Assert.True(Directory.Exists(first));
    }

    [Theory]
    [InlineData("1.02.0")]
    [InlineData("1.2")]
    [InlineData("1.2.0-beta")]
    [InlineData("2147483648.2.0")]
    public async Task Noncanonical_versions_are_rejected_before_metadata_creation(string version)
    {
        CreateSource();
        var result = await RunAsync("Assert-DesktopSharedPackageVersion $InvalidVersion", invalidVersion: version);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_root, "package")));
    }

    [Fact]
    public async Task Native_version_validation_reads_x64_resource_without_running_the_executable()
    {
        var source = CreateSource();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "DistributionResources", "DistributionResourcesA.exe"), Path.Combine(source, "TOOL-LOCAL.exe"), true);
        var success = await RunAsync("Test-DesktopPublishedIdentity $Source '1.2.0' 7 | ConvertTo-Json -Compress");
        Assert.True(success.ExitCode == 0, success.Error + Environment.NewLine + success.Output);
        var mismatch = await RunAsync("Test-DesktopPublishedIdentity $Source '1.2.0' 8");
        Assert.NotEqual(0, mismatch.ExitCode);
        Assert.Contains("native version/build", mismatch.Error);
    }

    [Fact]
    public async Task Insufficient_disk_budget_is_rejected_without_creating_a_package()
    {
        CreateSource();
        var result = await RunAsync("Assert-DesktopPublishedFreeSpace -Path $Package -RequiredBytes ([long]::MaxValue)");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Insufficient free disk space", result.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "package")));
    }

    [Fact]
    public async Task Windows_PowerShell_File_resolves_default_directories_after_parameter_binding()
    {
        var source = CreateSource();
        var isolatedRepo = Path.Combine(_root, "script fixture & phụ đề");
        var scripts = Path.Combine(isolatedRepo, "scripts");
        var web = Path.Combine(isolatedRepo, "TOOL-LOCAL", "Web", "dist");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(web);
        File.WriteAllText(Path.Combine(web, "index.html"), "controlled fixture");
        foreach (var name in new[] { "New-DesktopPublishedPackage.ps1", "DesktopPublishedPackage.psm1", "DesktopOfflinePackage.psm1",
                     "DesktopExternalRelease.psm1", "DesktopAuthenticode.psm1", "WebView2Prerequisite.psm1" })
            File.Copy(Path.Combine(FindRepository(), "scripts", name), Path.Combine(scripts, name));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = _root
        };
        // Match MSBuild Exec's real Windows PowerShell -File invocation. Neither
        // optional directory is passed; parameter defaults must not use an empty
        // PSScriptRoot during parameter binding in Windows PowerShell 5.1.
        foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                     Path.Combine(scripts, "New-DesktopPublishedPackage.ps1"), "-PublishRoot", source, "-Version", "1.2.0", "-BuildNumber", "7" })
            start.ArgumentList.Add(value);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        var error = await stderr;
        _ = await stdout;
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("not a Windows executable", error);
        Assert.DoesNotContain("empty string", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(isolatedRepo, "artifacts", "releases")));
    }

    [Fact]
    public async Task Verified_zip_releases_only_its_own_payload_then_can_be_extracted_and_reverified()
    {
        var (payload, archive) = CreateReleasablePayload();
        var result = await RunAsync("""
            New-DesktopSharedPackageMetadata $First '1.2.0' 7 'https://fixture.invalid/'
            New-DesktopPackageZip $First $Second
            $hash = (Get-FileHash -LiteralPath $Second -Algorithm SHA256).Hash.ToLowerInvariant()
            Remove-DesktopVerifiedPackagePayload -PayloadRoot $First -OutputRoot $Output -ArchivePath $Second -Version '1.2.0' -BuildNumber 7 -ExpectedSha256 $hash | Out-Null
            if (Test-Path -LiteralPath $First) { throw 'Owned payload was not released.' }
            $exclusive = [IO.File]::Open($Second, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $exclusive.Dispose()
            Expand-DesktopPackageZip $Second $Extracted | Out-Null
            Test-DesktopPackageInventory $Extracted | ConvertTo-Json -Compress
            """, payload, archive);
        Assert.True(result.ExitCode == 0, result.Error + Environment.NewLine + result.Output);
        Assert.False(Directory.Exists(payload));
        Assert.True(File.Exists(archive));
        Assert.True(File.Exists(Path.Combine(_root, "extracted", "TOOL-LOCAL.exe")));
        Assert.Equal("Existing release preserved", File.ReadAllText(Path.Combine(_root, "output", "old-release.zip")));
    }

    [Theory]
    [InlineData("wrong-hash")]
    [InlineData("archive-entry-corrupt")]
    [InlineData("inventory-corrupt")]
    [InlineData("wrong-version")]
    [InlineData("unowned-payload")]
    [InlineData("different-run")]
    [InlineData("payload-junction")]
    public async Task Unverified_or_unowned_payload_is_preserved_before_any_cleanup(string mutation)
    {
        var (payload, archive) = CreateReleasablePayload();
        var body = """
            New-DesktopSharedPackageMetadata $First '1.2.0' 7 'https://fixture.invalid/'
            New-DesktopPackageZip $First $Second
            $hash = (Get-FileHash -LiteralPath $Second -Algorithm SHA256).Hash.ToLowerInvariant()
            $requestedVersion = '1.2.0'
            """;
        body += mutation switch
        {
            "wrong-hash" => "\n$hash = '" + new string('0', 64) + "'\n",
            "archive-entry-corrupt" => """

                Add-Type -AssemblyName System.IO.Compression.FileSystem
                $zip = [IO.Compression.ZipFile]::Open($Second, [IO.Compression.ZipArchiveMode]::Update)
                try {
                    $entry = $zip.GetEntry('appsettings.json'); $entry.Delete()
                    $entry = $zip.CreateEntry('appsettings.json')
                    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
                    try { $writer.Write('[]') } finally { $writer.Dispose() }
                } finally { $zip.Dispose() }
                $hash = (Get-FileHash -LiteralPath $Second -Algorithm SHA256).Hash.ToLowerInvariant()

                """,
            "inventory-corrupt" => "\nAdd-Content -LiteralPath (Join-Path $First 'appsettings.json') -Value 'tampered'\n",
            "wrong-version" => """

                $requestedVersion = '1.2.1'
                $newPath = Join-Path (Split-Path -Parent $Second) 'VideoMaker-1.2.1-7-win-x64.zip'
                Move-Item -LiteralPath $Second -Destination $newPath
                $Second = $newPath

                """,
            "unowned-payload" => """

                $renamed = Join-Path $Output 'user-publish'
                Move-Item -LiteralPath $First -Destination $renamed
                $First = $renamed

                """,
            "different-run" => """

                $otherRun = Join-Path $Output ('.vm-package-' + [Guid]::NewGuid().ToString('N') + '.part')
                [IO.Directory]::CreateDirectory($otherRun) | Out-Null
                $newPath = Join-Path $otherRun ([IO.Path]::GetFileName($Second))
                Move-Item -LiteralPath $Second -Destination $newPath
                $Second = $newPath

                """,
            "payload-junction" => """

                $outside = Join-Path (Split-Path -Parent $Output) 'outside'
                [IO.Directory]::CreateDirectory($outside) | Out-Null
                [IO.File]::WriteAllText((Join-Path $outside 'keep.txt'), 'Outside data preserved')
                New-Item -ItemType Junction -Path (Join-Path $First 'linked') -Target $outside | Out-Null
                [IO.File]::WriteAllText((Join-Path $First 'package-inventory.json'), 'Invalid JSON must never be read before the reparse scan.')

                """,
            _ => throw new InvalidOperationException("Unknown fixture mutation.")
        };
        body += "\nRemove-DesktopVerifiedPackagePayload -PayloadRoot $First -OutputRoot $Output -ArchivePath $Second -Version $requestedVersion -BuildNumber 7 -ExpectedSha256 $hash\n";
        var result = await RunAsync(body, payload, archive);
        Assert.NotEqual(0, result.ExitCode);
        var expectedError = mutation switch
        {
            "wrong-hash" => "ZIP changed before releasing",
            "archive-entry-corrupt" => "entry SHA-256 does not match",
            "inventory-corrupt" => "inventory size/SHA-256 mismatch",
            "wrong-version" => "native version/build does not match",
            "unowned-payload" => "unowned desktop package payload",
            "different-run" => "same desktop packaging run",
            "payload-junction" => "reparse",
            _ => throw new InvalidOperationException("Unknown fixture mutation.")
        };
        Assert.Contains(expectedError, result.Error);
        var expectedPayload = mutation == "unowned-payload" ? Path.Combine(_root, "output", "user-publish") : payload;
        Assert.True(File.Exists(Path.Combine(expectedPayload, "TOOL-LOCAL.exe")));
        Assert.True(File.Exists(Path.Combine(expectedPayload, "package-inventory.json")));
        Assert.Equal("Existing release preserved", File.ReadAllText(Path.Combine(_root, "output", "old-release.zip")));
        Assert.False(Directory.Exists(Path.Combine(_root, "extracted")));
        if (mutation == "payload-junction")
        {
            Assert.Equal("Outside data preserved", File.ReadAllText(Path.Combine(_root, "outside", "keep.txt")));
            Directory.Delete(Path.Combine(expectedPayload, "linked"));
        }
    }

    private (string Payload, string Archive) CreateReleasablePayload()
    {
        var output = Path.Combine(_root, "output");
        var runId = Guid.NewGuid().ToString("N");
        var payload = Path.Combine(output, ".vm-package-payload-" + runId + ".part");
        var archiveRoot = Path.Combine(output, ".vm-package-" + runId + ".part");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(archiveRoot);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "DistributionResources", "DistributionResourcesA.exe"), Path.Combine(payload, "TOOL-LOCAL.exe"));
        File.WriteAllText(Path.Combine(payload, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(output, "old-release.zip"), "Existing release preserved");
        return (payload, Path.Combine(archiveRoot, "VideoMaker-1.2.0-7-win-x64.zip"));
    }

    private string CreateSource()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "TOOL-LOCAL.exe"), "Fixture only: never execute.");
        File.WriteAllText(Path.Combine(source, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(source, "symbols.pdb"), "debug fixture");
        File.WriteAllText(Path.Combine(source, "docs.xml"), "documentation fixture");
        return source;
    }

    private string CreateStage(string value, DateTimeOffset? timestamp = null)
    {
        var output = Path.Combine(_root, "output");
        var stage = Path.Combine(output, ".vm-package-" + Guid.NewGuid().ToString("N") + ".part");
        Directory.CreateDirectory(stage);
        const string name = "VideoMaker-1.2.0-7-win-x64.zip";
        var zipPath = Path.Combine(stage, name);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("TOOL-LOCAL.exe");
            entry.LastWriteTime = timestamp ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(value);
        }
        var hash = Hash(zipPath);
        File.WriteAllText(Path.Combine(stage, "checksums.sha256"), hash + "  " + name + Environment.NewLine);
        File.WriteAllText(Path.Combine(stage, "package-report.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, deliveryMode = "SharedVps", version = "1.2.0", buildNumber = 7,
            package = name, sizeBytes = new FileInfo(zipPath).Length, sha256 = hash,
            setupBuilt = false, productionQualified = false, packageVerified = true
        }));
        return stage;
    }

    private async Task<ProcessResult> RunAsync(string body, string first = "", string second = "", string invalidVersion = "")
    {
        Directory.CreateDirectory(_root);
        var repo = FindRepository();
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module '{{Escape(Path.Combine(repo, "scripts", "DesktopPublishedPackage.psm1"))}}' -Force
            Import-Module '{{Escape(Path.Combine(repo, "scripts", "DesktopOfflinePackage.psm1"))}}' -Force
            $Source = '{{Escape(Path.Combine(_root, "source"))}}'
            $Package = '{{Escape(Path.Combine(_root, "package"))}}'
            $Zip = '{{Escape(Path.Combine(_root, "package.zip"))}}'
            $Extracted = '{{Escape(Path.Combine(_root, "extracted"))}}'
            $List = '{{Escape(Path.Combine(_root, "files.txt"))}}'
            $Output = '{{Escape(Path.Combine(_root, "output"))}}'
            $First = '{{Escape(first)}}'
            $Second = '{{Escape(second)}}'
            $InvalidVersion = '{{Escape(invalidVersion)}}'
            {{body}}
            """;
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private static string Escape(string value) => value.Replace("'", "''");
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string FindRepository()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "scripts", "DesktopPublishedPackage.psm1"))) return current.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
