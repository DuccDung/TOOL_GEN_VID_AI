using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_TESTS.Updates;

public sealed class ModelDistributionHandoverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-model-handover-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Copy_verifies_pinned_source_and_creates_independent_destination()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "source.bin");
        var destination = Path.Combine(_root, "bộ bàn giao", "model.bin");
        await File.WriteAllBytesAsync(source, Encoding.UTF8.GetBytes("fixture model identity"));
        var hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(source)));
        var result = await RunAsync("""
            $stamp = Copy-ModelVerifiedFile -Source $Source -Destination $Destination -ExpectedSize 22 -ExpectedSha256 $Identity
            [IO.File]::WriteAllText($Source, 'source changed after copy')
            $stamp | ConvertTo-Json -Compress
            """, source, destination, hash);
        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output.Trim());
        Assert.Equal(hash, report.RootElement.GetProperty("sha256").GetString());
        Assert.Equal("fixture model identity", await File.ReadAllTextAsync(destination));
    }

    [Theory]
    [InlineData("size")]
    [InlineData("sha256")]
    [InlineData("sha512")]
    public async Task Invalid_source_is_rejected_before_creating_destination(string mismatch)
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "source.bin");
        var destination = Path.Combine(_root, "not-created", "model.bin");
        await File.WriteAllTextAsync(source, "fixture");
        var identity = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(source)));
        var arguments = mismatch switch
        {
            "size" => "-ExpectedSize 6 -ExpectedSha256 $Identity",
            "sha256" => "-ExpectedSize 7 -ExpectedSha256 ('0' * 64)",
            _ => "-ExpectedSize 7 -ExpectedSha512Base64 ([Convert]::ToBase64String((New-Object byte[] 64)))"
        };
        var result = await RunAsync("Copy-ModelVerifiedFile -Source $Source -Destination $Destination " + arguments, source, destination, identity);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.GetDirectoryName(destination)));
    }

    [Fact]
    public async Task Existing_candidate_files_are_never_overwritten()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "source.bin");
        var destination = Path.Combine(_root, "existing.bin");
        await File.WriteAllTextAsync(source, "fixture");
        await File.WriteAllTextAsync(destination, "keep customer bytes");
        var identity = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(source)));
        var result = await RunAsync("Copy-ModelVerifiedFile -Source $Source -Destination $Destination -ExpectedSize 7 -ExpectedSha256 $Identity", source, destination, identity);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("keep customer bytes", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task Piper_verifies_every_entry_without_extracting_or_probing_runtime()
    {
        var (archive, manifest, size) = CreatePiperFixture("model/giọng Việt.onnx", "python/python.exe");
        var result = await RunPiperAsync(archive, manifest, size);
        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output.Trim());
        Assert.Equal(2, report.RootElement.GetProperty("filesVerified").GetInt32());
        Assert.False(report.RootElement.GetProperty("runtimeProbed").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(_root, "model")));
        Assert.False(Directory.Exists(Path.Combine(_root, "python")));
    }

    [Theory]
    [InlineData("../escape.bin")]
    [InlineData("C:/escape.bin")]
    [InlineData("folder\\escape.bin")]
    [InlineData("CON.txt")]
    [InlineData("folder./escape.bin")]
    [InlineData(".venv/Scripts/python.exe")]
    [InlineData("probe.json")]
    public async Task Piper_rejects_unsafe_or_machine_specific_payload_entries(string name)
    {
        var (archive, manifest, size) = CreatePiperFixture(name);
        var result = await RunPiperAsync(archive, manifest, size);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_root, "escape.bin")));
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("extra")]
    [InlineData("collision")]
    [InlineData("symlink")]
    [InlineData("forged-size")]
    public async Task Piper_rejects_archive_content_outside_approved_manifest(string mutation)
    {
        var names = mutation == "collision" ? new[] { "same.bin", "SAME.bin" } : new[] { "good.bin" };
        var (archivePath, manifestPath, size) = CreatePiperFixture(names);
        if (mutation is "extra" or "symlink")
        {
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
            if (mutation == "extra")
            {
                using var stream = archive.CreateEntry("not-in-manifest.bin").Open();
                stream.WriteByte(1);
            }
            else archive.GetEntry("good.bin")!.ExternalAttributes = unchecked((int)(0xa1ffu << 16));
        }
        if (mutation == "checksum")
        {
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
            archive.GetEntry("good.bin")!.Delete();
            using var stream = archive.CreateEntry("good.bin").Open();
            stream.Write(Encoding.UTF8.GetBytes(new string('z', 18)));
        }
        if (mutation == "forged-size")
        {
            var bytes = await File.ReadAllBytesAsync(archivePath);
            for (var offset = 0; offset <= bytes.Length - 46; offset++)
            {
                if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) != 0x02014b50) continue;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24), 3);
                break;
            }
            await File.WriteAllBytesAsync(archivePath, bytes);
        }
        var result = await RunPiperAsync(archivePath, manifestPath, size);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Source_snapshot_scope_excludes_secrets_caches_and_old_builds()
    {
        Directory.CreateDirectory(_root);
        var result = await RunAsync("""
            $unsafe = @('TOOL-LOCAL/appsettings.user.json', 'TOOL-LOCAL/appsettings.json', '.tmp/worker.cs',
                'output/result.cs', 'artifacts/runtime/check.py', 'TOOL-LOCAL/bin/Release/a.cs',
                'TOOL-LOCAL/obj/a.cs', 'TOOL-LOCAL/Web/dist/a.js', 'TOOL-LOCAL/Web/node_modules/a.ts',
                'third_party/translation/runtime-test/probe.json', 'scripts/private-secrets.ps1',
                'database/production-connectionstring.txt', 'TOOL-LOCAL/.venv/a.py')
            foreach ($path in $unsafe) { if (Test-ModelSnapshotPath $path) { throw 'Unsafe source snapshot scope.' } }
            foreach ($path in @('TOOL-LOCAL/Program.cs', 'scripts/New-ModelDistributionHandover.ps1',
                'third_party/voice/PIPER_OFFLINE_APPROVED.json', 'TOOL-VIETSUB-TRANSLATION-WORKER/TOOL-VIETSUB-TRANSLATION-WORKER.csproj')) {
                if (-not (Test-ModelSnapshotPath $path)) { throw 'Required source identity omitted.' }
            }
            'safe'
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("safe", result.Output.Trim());
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("unsafe-path")]
    public async Task Handover_inventory_detects_changes_and_rejects_path_escape(string mutation)
    {
        var candidate = Path.Combine(_root, "candidate");
        Directory.CreateDirectory(candidate);
        var edit = mutation switch
        {
            "changed" => "[IO.File]::WriteAllText((Join-Path $Source 'model.bin'), 'changed')",
            "missing" => "[IO.File]::Delete((Join-Path $Source 'model.bin'))",
            "unsafe-path" => "$inventory.files[0].path = '../model.bin'; [IO.File]::Delete((Join-Path $Source 'inventory.json')); Write-ModelDistributionJson (Join-Path $Source 'inventory.json') $inventory",
            _ => "[IO.File]::WriteAllText((Join-Path $Source 'extra.bin'), 'extra')"
        };
        var result = await RunAsync("""
            [IO.File]::WriteAllText((Join-Path $Source 'model.bin'), 'fixture')
            $stamp = Get-ModelFileStamp (Join-Path $Source 'model.bin')
            $inventory = [ordered]@{ schemaVersion = 1; state = 'ArtifactVerified'; candidateId = 'fixture-01';
                excludes = @('inventory.json', 'checksums.sha256'); files = @([pscustomobject]@{
                    path = 'model.bin'; sizeBytes = $stamp.sizeBytes; sha256 = $stamp.sha256 }) }
            Write-ModelDistributionJson (Join-Path $Source 'inventory.json') $inventory
            Test-ModelHandoverInventory -Root $Source | Out-Null
            """ + "\n" + edit + "\nTest-ModelHandoverInventory -Root $Source", candidate);
        Assert.NotEqual(0, result.ExitCode);
    }

    private (string Archive, string Manifest, long Size) CreatePiperFixture(params string[] names)
    {
        Directory.CreateDirectory(_root);
        var archive = Path.Combine(_root, "piper.zip");
        var manifest = Path.Combine(_root, "manifest.json");
        var bytes = Encoding.UTF8.GetBytes("payload fixture 01");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            foreach (var name in names)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            files = names.Select(path => new { path, size = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) }),
            installedFiles = new[] { new { path = ".venv/Scripts/python.exe", size = 1, sha256 = new string('0', 64) } }
        }));
        return (archive, manifest, bytes.Length * names.Length);
    }

    private Task<(int ExitCode, string Output, string Error)> RunPiperAsync(string archive, string manifest, long size) =>
        RunAsync("$manifest = Get-Content -LiteralPath $Destination -Raw -Encoding UTF8 | ConvertFrom-Json\n" +
            "Test-ModelPiperArchive -ArchivePath $Source -Manifest $manifest -ExpectedExpandedBytes ([long]$Identity) | ConvertTo-Json -Compress", archive, manifest, size.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string body, string source = "", string destination = "", string identity = "")
    {
        Directory.CreateDirectory(_root);
        if (string.IsNullOrEmpty(source)) source = _root;
        if (string.IsNullOrEmpty(destination)) destination = Path.Combine(_root, "unused");
        if (string.IsNullOrEmpty(identity)) identity = "unused";
        var script = Path.Combine(_root, "run-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(script, """
            param($Module, $Source, $Destination, $Identity)
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
            Import-Module $Module -Force
            """ + "\n" + body, new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-Module", Path.Combine(FindRepositoryRoot(), "scripts", "ModelDistributionHandover.psm1"),
            "-Source", source, "-Destination", destination, "-Identity", identity }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Model distribution tests require the source checkout.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
