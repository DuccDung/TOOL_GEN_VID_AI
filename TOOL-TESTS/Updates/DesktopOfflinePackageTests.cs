using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Updates;

public sealed class DesktopOfflinePackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-offline-package-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Streaming_zip_preserves_unicode_hidden_files_and_inventory()
    {
        var package = CreatePackage();
        File.WriteAllText(Path.Combine(package, ".hidden"), "hidden fixture");
        File.SetAttributes(Path.Combine(package, ".hidden"), FileAttributes.Hidden);
        var result = await RunAsync("""
            New-DesktopPackageInventory $Package
            New-DesktopPackageZip $Package $Zip
            Expand-DesktopPackageZip $Zip $Extracted | Out-Null
            Test-DesktopPackageInventory $Extracted | ConvertTo-Json -Compress
            """, package);
        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output.Trim());
        Assert.True(report.RootElement.GetProperty("InventoryVerified").GetBoolean());
        Assert.Equal("phụ đề", File.ReadAllText(Path.Combine(_root, "extracted", "có dấu", "cue.txt")));
        Assert.Equal("hidden fixture", File.ReadAllText(Path.Combine(_root, "extracted", ".hidden")));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("extra")]
    public async Task Inventory_rejects_changed_missing_or_untracked_files(string mutation)
    {
        var package = CreatePackage();
        var edit = mutation switch
        {
            "changed" => "[IO.File]::WriteAllText((Join-Path $Package 'có dấu/cue.txt'), 'tampered')",
            "missing" => "Remove-Item -LiteralPath (Join-Path $Package 'có dấu/cue.txt')",
            _ => "[IO.File]::WriteAllText((Join-Path $Package 'untracked.txt'), 'extra')"
        };
        var result = await RunAsync("New-DesktopPackageInventory $Package\n" + edit + "\nTest-DesktopPackageInventory $Package", package);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("inventory", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/escape.txt")]
    [InlineData("C:/escape.txt")]
    [InlineData("folder\\escape.txt")]
    [InlineData("folder/../escape.txt")]
    [InlineData("CON.txt")]
    [InlineData("folder./escape.txt")]
    public async Task Unsafe_zip_names_are_rejected_before_extracting_any_file(string name)
    {
        var package = CreatePackage();
        CreateZip("good.txt", name);
        var result = await RunAsync("Expand-DesktopPackageZip $Zip $Extracted", package);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_root, "extracted")));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Fact]
    public async Task Case_collisions_are_rejected_before_extracting()
    {
        var package = CreatePackage();
        CreateZip("same.txt", "SAME.txt");
        var result = await RunAsync("Expand-DesktopPackageZip $Zip $Extracted", package);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_root, "extracted")));
    }

    [Fact]
    public async Task Existing_customer_files_and_existing_zip_are_never_overwritten()
    {
        var package = CreatePackage();
        CreateZip("new.txt");
        var existingZip = File.ReadAllBytes(Path.Combine(_root, "package.zip"));
        var create = await RunAsync("New-DesktopPackageZip $Package $Zip", package);
        Assert.NotEqual(0, create.ExitCode);
        Assert.Equal(existingZip, File.ReadAllBytes(Path.Combine(_root, "package.zip")));
        Directory.CreateDirectory(Path.Combine(_root, "extracted"));
        File.WriteAllText(Path.Combine(_root, "extracted", "customer.txt"), "keep");
        var extract = await RunAsync("Expand-DesktopPackageZip $Zip $Extracted", package);
        Assert.NotEqual(0, extract.ExitCode);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_root, "extracted", "customer.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "extracted", "new.txt")));
    }

    [Fact]
    public async Task Extraction_does_not_write_past_a_forged_uncompressed_size()
    {
        var package = CreatePackage();
        var zip = Path.Combine(_root, "package.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("large.txt", CompressionLevel.Optimal).Open());
            writer.Write(new string('x', 65536));
        }
        var bytes = File.ReadAllBytes(zip);
        for (var offset = 0; offset < bytes.Length - 46; offset++)
        {
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) != 0x02014b50) continue;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24), 32);
            break;
        }
        File.WriteAllBytes(zip, bytes);
        var result = await RunAsync("Expand-DesktopPackageZip $Zip $Extracted", package);
        Assert.NotEqual(0, result.ExitCode);
        var target = Path.Combine(_root, "extracted", "large.txt");
        Assert.True(!File.Exists(target) || new FileInfo(target).Length <= 32);
    }

    [Fact]
    public async Task Invalid_qwen_is_rejected_before_copying_into_the_package()
    {
        var package = CreatePackage();
        File.WriteAllText(Path.Combine(package, "fake.gguf"), "not the approved model");
        var result = await RunAsync("Add-DesktopQwenModel (Join-Path $Package 'fake.gguf') $Package", package);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(package, "third_party")));
    }

    [Fact]
    public async Task Packaging_definition_matches_the_runtime_model()
    {
        var result = await RunAsync("Get-DesktopQwenDefinition | ConvertTo-Json -Compress", CreatePackage());
        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output.Trim());
        var model = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        Assert.Equal(model.ModelSizeBytes, report.RootElement.GetProperty("SizeBytes").GetInt64());
        Assert.Equal(model.ModelSha256, report.RootElement.GetProperty("Sha256").GetString());
        Assert.EndsWith(model.ComponentId + "/" + model.EngineVersion + "/" + model.ModelFileName,
            report.RootElement.GetProperty("RelativePath").GetString());
    }

    [Theory]
    [InlineData("relative-workspace")]
    [InlineData("existing-workspace")]
    public async Task Qwen_diagnostic_rejects_unowned_workspace_without_changing_it(string kind)
    {
        Directory.CreateDirectory(_root);
        var workspace = kind == "relative-workspace" ? "relative-workspace" : Path.Combine(_root, "existing");
        if (kind == "existing-workspace")
        {
            Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(workspace, "customer.txt"), "keep");
        }
        var exitCode = await QwenOfflineCommand.RunAsync(["--check-qwen-offline", "--workspace", workspace], _root);
        Assert.Equal(2, exitCode);
        if (kind == "existing-workspace")
        {
            Assert.Equal("keep", File.ReadAllText(Path.Combine(workspace, "customer.txt")));
            Assert.Single(Directory.GetFileSystemEntries(workspace));
        }
    }

    [Fact]
    public void Diagnostic_component_root_requires_owned_temporary_workspace_and_explicit_grant()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "vm-qwen-offline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var root = VietsubTranslationDiagnosticWorkspace.Initialize(workspace);
            Assert.True(VietsubTranslationDiagnosticWorkspace.IsApprovedComponentRoot(root, root));
            Assert.False(VietsubTranslationDiagnosticWorkspace.IsApprovedComponentRoot(root, null));
            Assert.False(VietsubTranslationDiagnosticWorkspace.IsApprovedComponentRoot(Path.Combine(root, "foreign"), root));
            File.Delete(Path.Combine(workspace, VietsubTranslationDiagnosticWorkspace.MarkerFileName));
            Assert.False(VietsubTranslationDiagnosticWorkspace.IsApprovedComponentRoot(root, root));
        }
        finally { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
    }

    [Fact]
    public async Task Real_worker_accepts_owned_diagnostic_root_but_still_rejects_unapproved_model_bytes()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "vm-qwen-offline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var root = VietsubTranslationDiagnosticWorkspace.Initialize(workspace);
            var model = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
            var modelPath = Path.Combine(root, model.ComponentId, model.EngineVersion, model.ModelFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
            File.WriteAllText(modelPath, "Invalid fixture: do not load weights.");
            await using var worker = new VietsubTranslationWorkerClient(
                VietsubTranslationWorkerClientOptions.CreateDefault() with
                {
                    EnvironmentVariables = new Dictionary<string, string?>
                    {
                        ["VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE"] = null,
                        [VietsubTranslationDiagnosticWorkspace.EnvironmentVariable] = root
                    }
                }, new SufficientMemoryProbe());
            var request = new VietsubTranslationWorkerLoadRequest(root, modelPath, model.ComponentId,
                model.EngineId, model.EngineVersion, model.ModelFileName, model.ModelSizeBytes, model.ModelSha256,
                VietsubTranslationWorkerProfiles.CreateSafeCpuProfile(Environment.ProcessorCount),
                VietsubTranslationResourceRequirements.StandardCpu);
            var error = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
                worker.LoadAsync(request, null, CancellationToken.None));
            Assert.Equal(VietsubTranslationErrorCodes.RuntimeInvalid, error.Code);
            Assert.Contains("kích thước", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task Worker_rejects_missing_or_corrupt_bundled_msvc_even_when_system_runtime_is_available(string mutation)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "_translation_worker");
        var copy = Path.Combine(_root, "worker");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(copy, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var runtime = Path.Combine(copy, "vcomp140.dll");
        if (mutation == "missing") File.Delete(runtime);
        else
        {
            using var stream = new FileStream(runtime, FileMode.Open, FileAccess.Write);
            stream.WriteByte(0);
        }
        await using var worker = new VietsubTranslationWorkerClient(
            VietsubTranslationWorkerClientOptions.CreateDefault() with
            {
                WorkerExecutablePath = Path.Combine(copy, "VideoMaker.Vietsub.TranslationWorker.exe"),
                EnvironmentVariables = new Dictionary<string, string?>
                { ["VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE"] = "backend-preflight" }
            }, new SufficientMemoryProbe());
        var model = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => worker.LoadAsync(
            new VietsubTranslationWorkerLoadRequest(_root, Path.Combine(_root, "unused.gguf"), model.ComponentId,
                model.EngineId, model.EngineVersion, model.ModelFileName, model.ModelSizeBytes, model.ModelSha256,
                VietsubTranslationWorkerProfiles.CreateSafeCpuProfile(Environment.ProcessorCount),
                VietsubTranslationResourceRequirements.StandardCpu), null, CancellationToken.None));
        Assert.Equal(VietsubTranslationErrorCodes.BackendLoadFailed, error.Code);
        Assert.Contains("Visual C++", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SufficientMemoryProbe : IVietsubTranslationMemoryProbe
    {
        public bool TryCapture(out VietsubTranslationMemorySnapshot snapshot, out string? error)
        {
            const ulong gib = 1024UL * 1024 * 1024;
            snapshot = new(16 * gib, 10 * gib, 32 * gib, 20 * gib, 25, DateTime.UtcNow);
            error = null;
            return true;
        }
    }

    [Fact]
    public async Task Customer_sql_provisioning_keeps_credentials_out_of_output_and_restricts_the_override_file()
    {
        var package = CreatePackage();
        File.WriteAllText(Path.Combine(package, "TOOL-LOCAL.exe"), "fixture; never executed");
        File.WriteAllText(Path.Combine(package, "appsettings.json"), "{}");
        const string fixturePassword = "Fixture-only-Not-A-Real-Secret";
        var result = await RunAsync("""
            $password = ConvertTo-SecureString 'Fixture-only-Not-A-Real-Secret' -AsPlainText -Force
            & (Join-Path (Split-Path $Module) 'Configure-CustomerSql.ps1') -PackageRoot $Package `
                -SqlServer sql.fixture.invalid -Port 1433 -Database Fixture -UserName vf_fixture -Password $password
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            $acl = Get-Acl -LiteralPath (Join-Path $Package 'appsettings.user.json')
            if (-not $acl.AreAccessRulesProtected) { throw 'Private override inherits access rules.' }
            """, package);
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(fixturePassword, result.Output + result.Error);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, "appsettings.user.json")));
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            document.RootElement.GetProperty("Database").GetProperty("ConnectionString").GetString());
        Assert.Equal(fixturePassword, builder.Password);
        Assert.Equal("tcp:sql.fixture.invalid,1433", builder.DataSource);
        Assert.False(builder.PersistSecurityInfo);
        Assert.False(builder.TrustServerCertificate);
        Assert.Equal(Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
    }

    [Fact]
    public async Task Customer_sql_provisioning_preserves_existing_private_configuration()
    {
        var package = CreatePackage();
        File.WriteAllText(Path.Combine(package, "TOOL-LOCAL.exe"), "fixture; never executed");
        File.WriteAllText(Path.Combine(package, "appsettings.json"), "{}");
        var existing = Path.Combine(package, "appsettings.user.json");
        File.WriteAllText(existing, "keep customer's original configuration");
        var result = await RunAsync("""
            & (Join-Path (Split-Path $Module) 'Configure-CustomerSql.ps1') -PackageRoot $Package `
                -SqlServer sql.fixture.invalid -Port 1433 -Database Fixture -WindowsAuthentication
            exit $LASTEXITCODE
            """, package);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("keep customer's original configuration", File.ReadAllText(existing));
        Assert.Empty(Directory.GetFiles(package, ".customer-sql-*.part"));
    }

    private string CreatePackage()
    {
        var package = Path.Combine(_root, "package có dấu");
        Directory.CreateDirectory(Path.Combine(package, "có dấu"));
        File.WriteAllText(Path.Combine(package, "có dấu", "cue.txt"), "phụ đề");
        return package;
    }

    private void CreateZip(params string[] names)
    {
        using var archive = ZipFile.Open(Path.Combine(_root, "package.zip"), ZipArchiveMode.Create);
        foreach (var name in names)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write("fixture");
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string body, string package)
    {
        var script = Path.Combine(_root, "run.ps1");
        await File.WriteAllTextAsync(script, """
            param($Module, $Package, $Zip, $Extracted)
            $ErrorActionPreference = 'Stop'
            Import-Module $Module -Force
            """ + "\n" + body, new System.Text.UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-Module", Path.Combine(FindRepositoryRoot(), "scripts", "DesktopOfflinePackage.psm1"),
            "-Package", package, "-Zip", Path.Combine(_root, "package.zip"), "-Extracted", Path.Combine(_root, "extracted") })
            start.ArgumentList.Add(argument);
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
        throw new InvalidOperationException("Offline packaging tests require the source checkout.");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }
}
