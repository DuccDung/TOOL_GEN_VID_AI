using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TOOL_TESTS.Updates;

public sealed class ModelSetupPreflightTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-preflight-" + Guid.NewGuid().ToString("N"));
    private string App => Path.Combine(_root, "app");
    private string Local => Path.Combine(_root, "local");
    private string Cache => Path.Combine(_root, "cache");
    private string Workspace => Path.Combine(_root, "workspace");
    private const string ApprovedHash = "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5";

    [Fact]
    public async Task Inventory_merges_overrides_without_exposing_credentials_or_changing_files()
    {
        Prepare();
        var artifact = Path.Combine(Cache, "artifacts", ApprovedHash, "Qwen3-4B-Q4_K_M.gguf.part");
        Write(artifact, "keep resumable prefix");
        var token = Path.Combine(Local, "ToolGenPostVideo", "auth-token.bin");
        Write(token, "private-token-fixture");
        var before = Snapshot();
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("private-token-fixture", result.Output);
        Assert.DoesNotContain("private-password-fixture", result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var report = document.RootElement;
        Assert.Equal("InventoryOnly", report.GetProperty("assessment").GetString());
        Assert.False(report.GetProperty("cleanupAuthorized").GetBoolean());
        Assert.False(report.GetProperty("serverVerified").GetBoolean());
        Assert.Equal("Development", report.GetProperty("channel").GetString());
        Assert.Equal(Cache, report.GetProperty("cacheRoot").GetString());
        Assert.Contains(report.GetProperty("candidates").EnumerateArray(), candidate =>
            candidate.GetProperty("path").GetString() == artifact && candidate.GetProperty("exists").GetBoolean());
        Assert.DoesNotContain("MODEL_SERVER_DISABLED", report.GetProperty("blockers").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(token, report.GetProperty("protectedPaths").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Both_launch_anchors_are_checked_and_piper_choice_is_recorded_before_cleanup()
    {
        Prepare();
        var legacy = Path.Combine(Workspace, "vietsub", "components", "voice", "piper");
        Directory.CreateDirectory(legacy);
        var reuse = Path.Combine(_root, "cwd", "third_party", "translation", "runtime-test",
            "qwen3-4b-q4-k-m-cpu", "bc64014-llamasharp-0.27.0-adapter-1", "Qwen3-4B-Q4_K_M.gguf");
        Write(reuse, "fixture reuse only");
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var report = document.RootElement;
        Assert.Equal(legacy, report.GetProperty("piperSelectedRootBeforeCleanup").GetString());
        Assert.True(report.GetProperty("piperRootMustBeResolvedAgainAfterCleanup").GetBoolean());
        Assert.Contains(report.GetProperty("candidates").EnumerateArray(), candidate =>
            candidate.GetProperty("kind").GetString() == "qwen-reuse" && candidate.GetProperty("path").GetString() == reuse
            && candidate.GetProperty("exists").GetBoolean());
    }

    [Fact]
    public async Task Known_revocation_in_old_channel_is_not_bypassed_by_empty_new_scope()
    {
        Prepare();
        var state = Path.Combine(Cache, "trust", "stable.json");
        Write(state, JsonSerializer.Serialize(new { repositoryId = "videomaker-models", channel = "Stable", platform = "win-x64",
            sequence = 7, catalogSha256 = new string('a', 64), revokedArtifactSha256 = new[] { ApprovedHash } }));
        var before = Snapshot();
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var report = document.RootElement;
        Assert.Single(report.GetProperty("trustStates").EnumerateArray());
        Assert.Contains("APPROVED_ARTIFACT_KNOWN_REVOKED", report.GetProperty("blockers").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("{ invalid private-token-fixture")]
    [InlineData("{\"repositoryId\":\"videomaker-models\",\"channel\":\"Stable\",\"platform\":\"win-x64\",\"sequence\":\"private-token-fixture\",\"catalogSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}")]
    public async Task Corrupt_trust_is_blocked_without_printing_its_contents(string contents)
    {
        Prepare();
        Write(Path.Combine(Cache, "trust", "corrupt.json"), contents);
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("private-token-fixture", result.Output + result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Contains("TRUST_STATE_UNREADABLE_OR_INVALID", document.RootElement.GetProperty("blockers").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task Workspace_is_trimmed_before_inventory_like_the_desktop()
    {
        Prepare();
        Write(Path.Combine(App, "appsettings.user.json"), JsonSerializer.Serialize(new {
            Storage = new { WorkspaceRoot = "  " + Workspace + "  " }
        }));
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Contains(document.RootElement.GetProperty("candidates").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == "piper-legacy" && item.GetProperty("path").GetString() ==
            Path.Combine(Workspace, "vietsub", "components", "voice", "piper"));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    public async Task Revocation_lists_rejected_by_runtime_are_also_reported_as_invalid(string mutation)
    {
        Prepare();
        var revoked = mutation == "unknown" ? new[] { new string('b', 64) }
            : Enumerable.Repeat(ApprovedHash, mutation == "oversized" ? 4 : 2).ToArray();
        Write(Path.Combine(Cache, "trust", "invalid.json"), JsonSerializer.Serialize(new { repositoryId = "videomaker-models",
            channel = "Stable", platform = "win-x64", sequence = 7, catalogSha256 = new string('a', 64), revokedArtifactSha256 = revoked }));
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Contains("TRUST_STATE_UNREADABLE_OR_INVALID", document.RootElement.GetProperty("blockers").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task Invalid_settings_types_are_rejected_without_disclosing_the_value()
    {
        Prepare();
        Write(Path.Combine(App, "appsettings.user.json"), """{"ModelDistribution":{"Enabled":"private-token-fixture"}}""");
        var result = await RunAsync();
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("private-token-fixture", result.Output + result.Error);
    }

    [Fact]
    public async Task Report_refuses_to_overwrite_an_existing_file()
    {
        Prepare();
        var destination = Path.Combine(_root, "report.json");
        Write(destination, "keep original evidence");
        var result = await RunAsync("Write-ModelDistributionJson -Path (Join-Path $Fixture 'report.json') -Value $report");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("keep original evidence", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task Static_archive_inventory_uses_pinned_origin_and_setup_cache_without_server_catalog_or_mutation()
    {
        PrepareStatic();
        var definition = TOOL_SHARED.Distribution.StaticModels.ApprovedStaticModelBundle.Current;
        var cache = Path.Combine(Local, "ToolGenPostVideo", "system-setup", "static-model-cache");
        var part = Path.Combine(cache, definition.ArchiveSha256, "archive.zip.part");
        Write(part, "keep resumable static prefix");
        var before = Snapshot();
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("private-password-fixture", result.Output + result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var report = document.RootElement;
        Assert.Equal("StaticArchive", report.GetProperty("modelDeliveryMode").GetString());
        Assert.Equal(cache, report.GetProperty("cacheRoot").GetString());
        Assert.Equal(definition.ArchiveUrl.AbsoluteUri, report.GetProperty("staticModelBundle").GetProperty("archiveUrl").GetString());
        Assert.Equal(definition.ArchiveSha256, report.GetProperty("staticModelBundle").GetProperty("archiveSha256").GetString());
        Assert.False(report.GetProperty("actualCompilePolicyVerified").GetBoolean());
        Assert.False(report.GetProperty("runtimeProbed").GetBoolean());
        var blockers = report.GetProperty("blockers").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.DoesNotContain("MODEL_SERVER_DISABLED", blockers);
        Assert.DoesNotContain("INVALID_MODEL_ORIGIN", blockers);
        Assert.Contains(report.GetProperty("candidates").EnumerateArray(), candidate =>
            candidate.GetProperty("path").GetString() == part && candidate.GetProperty("exists").GetBoolean());
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Static_archive_invalid_overrides_are_reported_without_using_or_disclosing_them()
    {
        PrepareStatic(new { Mode = "StaticArchive", Enabled = true,
            BaseUrl = "https://private-token-fixture.invalid/", CacheRoot = Path.Combine(_root, "private-token-fixture") });
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("private-token-fixture", result.Output + result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Contains("STATIC_ARCHIVE_SETTINGS_OVERRIDE_NOT_ALLOWED",
            document.RootElement.GetProperty("blockers").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task Static_archive_reads_known_revocation_from_legacy_trust_and_protects_transaction_metadata()
    {
        PrepareStatic();
        var legacyTrust = Path.Combine(Local, "ToolGenPostVideo", "components", "model-downloads", "trust");
        Write(Path.Combine(legacyTrust, "stable.json"), JsonSerializer.Serialize(new { repositoryId = "videomaker-models",
            channel = "Stable", platform = "win-x64", sequence = 7, catalogSha256 = new string('a', 64),
            revokedArtifactSha256 = new[] { ApprovedHash } }));
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Contains("APPROVED_ARTIFACT_KNOWN_REVOKED",
            document.RootElement.GetProperty("blockers").EnumerateArray().Select(item => item.GetString()));
        var protectedPaths = document.RootElement.GetProperty("protectedPaths").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains(legacyTrust, protectedPaths);
        Assert.Contains(Path.Combine(Local, "ToolGenPostVideo", "system-setup", "installer-transactions"), protectedPaths);
        Assert.Contains(Path.Combine(App, "static-setup-receipt.json"), protectedPaths);
    }

    [Fact]
    public async Task Static_archive_uses_global_piper_and_does_not_credit_developer_payloads_or_existing_parts()
    {
        PrepareStatic();
        Directory.CreateDirectory(Path.Combine(Workspace, "vietsub", "components", "voice", "piper"));
        var result = await RunAsync();
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var report = document.RootElement;
        Assert.Equal(Path.Combine(Local, "ToolGenPostVideo", "components", "voice", "piper"),
            report.GetProperty("piperSelectedRootBeforeCleanup").GetString());
        Assert.DoesNotContain(report.GetProperty("candidates").EnumerateArray(), candidate => candidate.GetProperty("kind").GetString() == "qwen-reuse");
        var definition = TOOL_SHARED.Distribution.StaticModels.ApprovedStaticModelBundle.Current;
        Assert.Equal(definition.ArchiveSizeBytes + definition.MaximumExpandedBytes + 64L * 1024 * 1024,
            report.GetProperty("disk").GetProperty("downloadAndExtractionRequiredBytes").GetInt64());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("disk").GetProperty("qwenDownloadRequiredBytes").ValueKind);
        Assert.False(report.GetProperty("disk").GetProperty("includesSetupRuntimeAndBackupBudget").GetBoolean());
    }

    private void PrepareStatic(object? distribution = null)
    {
        Prepare();
        Write(Path.Combine(App, "appsettings.user.json"), JsonSerializer.Serialize(new {
            ModelDistribution = distribution ?? new { Mode = "StaticArchive", Enabled = false },
            Features = new { VietsubLocalTranslationEnabled = true }
        }));
    }

    [Theory]
    [InlineData("StaticSetup", 3758096384L, "ReadyForHarness")]
    [InlineData("Full", 3758096384L, "Blocked")]
    [InlineData("Timeline", 4753190912L, "Blocked")]
    [InlineData("Timeline", 5290061824L, "ReadyForHarness")]
    [InlineData("StaticSetup", 3221225471L, "Blocked")]
    public async Task Harness_disk_preflight_blocks_timeline_before_execution_without_lowering_other_profile_guards(
        string profile, long availableBytes, string expected)
    {
        Prepare();
        var result = await RunAsync("""
            Import-Module (Join-Path $Repository 'scripts/StabilityResourcePreflight.psm1') -Force
            Get-StabilityDiskAssessment -Profile '__PROFILE__' -AvailableBytes __BYTES__ | ConvertTo-Json
            """.Replace("__PROFILE__", profile, StringComparison.Ordinal)
                .Replace("__BYTES__", availableBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(expected, document.RootElement.GetProperty("assessment").GetString());
        Assert.False(document.RootElement.GetProperty("includesModelInstallationBudget").GetBoolean());
        Assert.False(document.RootElement.GetProperty("includesCleanWindowsAcceptance").GetBoolean());
        Assert.True(document.RootElement.GetProperty("requiredBytes").GetInt64() >= 3L * 1024 * 1024 * 1024);
    }

    private void Prepare()
    {
        Directory.CreateDirectory(App);
        Directory.CreateDirectory(Local);
        Directory.CreateDirectory(Path.Combine(_root, "cwd"));
        Write(Path.Combine(App, "appsettings.json"), JsonSerializer.Serialize(new {
            Server = new { BaseUrl = "https://models.invalid/" }, Database = new { ConnectionString = "private-password-fixture" },
            Storage = new { WorkspaceRoot = Workspace }, Update = new { Channel = "Stable" },
            Features = new { VietsubEnabled = true, VietsubOcrEnabled = true, VietsubLocalTranslationEnabled = false, VietsubLocalVoiceEnabled = true }
        }));
        Write(Path.Combine(App, "appsettings.user.json"), JsonSerializer.Serialize(new {
            Update = new { Channel = "Development" }, ModelDistribution = new { Enabled = true, CacheRoot = Cache },
            Features = new { VietsubLocalTranslationEnabled = true }
        }));
    }

    private static void Write(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value, new UTF8Encoding(false));
    }

    private string[] Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetFileName(path).StartsWith("runner-", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal).Select(path => path + "|" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string? after = null)
    {
        var runner = Path.Combine(_root, "runner-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(runner, """
            param($Repository, $Fixture)
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
            Import-Module (Join-Path $Repository 'scripts/ModelSetupPreflight.psm1') -Force -DisableNameChecking
            Import-Module (Join-Path $Repository 'scripts/ModelDistributionHandover.psm1') -Force -DisableNameChecking
            $report = Get-ModelSetupPreflight -ApplicationRoot (Join-Path $Fixture 'app') -LocalApplicationData (Join-Path $Fixture 'local') -CurrentDirectory (Join-Path $Fixture 'cwd') -FixedDriveRoots @()
            """ + "\n" + (after ?? "$report | ConvertTo-Json -Depth 12"), new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner,
            "-Repository", FindRepositoryRoot(), "-Fixture", _root }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Source checkout is required.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
