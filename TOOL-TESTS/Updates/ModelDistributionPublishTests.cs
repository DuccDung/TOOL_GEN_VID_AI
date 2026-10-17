using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_TESTS.Updates;

public sealed class ModelDistributionPublishTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-model-publish-tests-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Bundled_mode_preserves_the_existing_local_translation_model_gate(bool translation, bool includeQwen, bool success)
    {
        var settings = WriteSettings(translation, false);
        var result = await ValidateAsync(settings, "Bundled", includeQwen: includeQwen);
        Assert.Equal(success, result.ExitCode == 0);
        if (success)
        {
            using var report = JsonDocument.Parse(result.Output.Trim());
            Assert.True(report.RootElement.GetProperty("PayloadsBundled").GetBoolean());
        }
    }

    [Fact]
    public async Task Server_mode_accepts_explicit_public_trust_and_excludes_model_payload_requirements()
    {
        var settings = WriteSettings(true, true);
        var trust = WritePublicTrust();
        var result = await ValidateAsync(settings, "Server", trust);
        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output.Trim());
        Assert.False(report.RootElement.GetProperty("PayloadsBundled").GetBoolean());
        Assert.Equal("fixture-models", report.RootElement.GetProperty("RepositoryId").GetString());
        Assert.Equal(1, report.RootElement.GetProperty("PublicKeyCount").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(trust))),
            report.RootElement.GetProperty("TrustRootSha256").GetString());
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("missing-trust")]
    [InlineData("http")]
    [InlineData("port")]
    [InlineData("loopback")]
    [InlineData("credentials")]
    [InlineData("query")]
    [InlineData("private-cache")]
    [InlineData("include-qwen")]
    [InlineData("qwen-path")]
    [InlineData("piper-path")]
    public async Task Server_mode_rejects_incomplete_or_unsafe_release_profiles(string kind)
    {
        var origin = kind switch
        {
            "http" => "http://models.fixture.invalid/",
            "port" => "https://models.fixture.invalid:8443/",
            "loopback" => "https://127.0.0.1/",
            "credentials" => "https://account:fixture@models.fixture.invalid/",
            "query" => "https://models.fixture.invalid/?catalog=fixture",
            _ => "https://models.fixture.invalid/"
        };
        var settings = WriteSettings(true, kind != "disabled", origin, kind == "private-cache" ? @"D:\DeveloperCache" : null);
        var trust = kind == "missing-trust" ? null : WritePublicTrust();
        var result = await ValidateAsync(settings, "Server", trust, kind == "include-qwen",
            qwenPath: kind == "qwen-path" ? Path.Combine(_root, "unexpected.gguf") : null,
            piperPath: kind == "piper-path" ? Path.Combine(_root, "unexpected-piper") : null);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("private")]
    [InlineData("rsa")]
    [InlineData("wrong-curve")]
    [InlineData("malformed")]
    [InlineData("unknown-field")]
    public async Task Trust_root_accepts_only_the_reviewed_public_P256_format(string mutation)
    {
        var settings = WriteSettings(true, true);
        var trust = WritePublicTrust();
        if (mutation == "empty")
            await File.WriteAllTextAsync(trust, """{"schemaVersion":1,"repositoryId":"fixture-models","publicKeys":{}}""");
        else if (mutation == "private")
            await File.WriteAllTextAsync(trust, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, repositoryId = "fixture-models",
                publicKeys = new Dictionary<string, string> { ["fixture-key"] = "-----BEGIN PRIVATE KEY-----\nfixture-only-no-secret\n-----END PRIVATE KEY-----" }
            }));
        else if (mutation == "rsa")
        {
            using var key = RSA.Create(2048);
            WriteTrustWithPem(trust, key.ExportSubjectPublicKeyInfoPem());
        }
        else if (mutation == "wrong-curve")
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            WriteTrustWithPem(trust, key.ExportSubjectPublicKeyInfoPem());
        }
        else if (mutation == "malformed") WriteTrustWithPem(trust, "-----BEGIN PUBLIC KEY-----\nAA==\n-----END PUBLIC KEY-----");
        else
            await File.WriteAllTextAsync(trust, """{"schemaVersion":1,"repositoryId":"fixture-models","publicKeys":{},"unknown":"rejected"}""");
        var result = await ValidateAsync(settings, "Server", trust);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("fixture-only-no-secret", result.Error);
    }

    [Fact]
    public async Task Published_server_package_verifies_pinned_worker_without_installing_Piper()
    {
        var settings = WriteSettings(true, true);
        var trust = WritePublicTrust();
        var publish = CreatePublishFixture();
        var result = await ValidateAsync(settings, "Server", trust, publishRoot: publish);
        Assert.Equal(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(publish, "components")));
        Assert.Empty(Directory.GetFiles(publish, "*.gguf", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("qwen")]
    [InlineData("piper")]
    [InlineData("worker")]
    [InlineData("lock")]
    [InlineData("definition")]
    public async Task Published_server_package_rejects_accidental_payloads_and_changed_runtime_contracts(string mutation)
    {
        var settings = WriteSettings(true, true);
        var trust = WritePublicTrust();
        var publish = CreatePublishFixture();
        var path = mutation switch
        {
            "qwen" => Path.Combine(publish, "unexpected.gguf"),
            "piper" => Path.Combine(publish, "piper-offline.zip"),
            "worker" => Path.Combine(publish, "workers", "piper_worker.py"),
            "lock" => Path.Combine(publish, "workers", "piper-requirements.lock"),
            _ => Path.Combine(publish, "third_party", "voice", "PIPER_OFFLINE_APPROVED.json")
        };
        await File.WriteAllTextAsync(path, "fixture changed bytes");
        var result = await ValidateAsync(settings, "Server", trust, publishRoot: publish);
        Assert.NotEqual(0, result.ExitCode);
    }

    private string WriteSettings(bool translation, bool distribution, string origin = "https://models.fixture.invalid/", string? cache = null)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "release-settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Features = new { VietsubLocalTranslationEnabled = translation },
            ModelDistribution = new { Enabled = distribution, BaseUrl = origin, CacheRoot = cache }
        }));
        return path;
    }

    private string WritePublicTrust()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "public-trust.json");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        WriteTrustWithPem(path, key.ExportSubjectPublicKeyInfoPem());
        return path;
    }

    private static void WriteTrustWithPem(string path, string pem) => File.WriteAllText(path, JsonSerializer.Serialize(new
    {
        schemaVersion = 1, repositoryId = "fixture-models", publicKeys = new Dictionary<string, string> { ["fixture-key"] = pem }
    }));

    private string CreatePublishFixture()
    {
        var publish = Path.Combine(_root, "publish");
        foreach (var relative in new[] { "TOOL-LOCAL/Vietsub/Voice/Workers/piper_worker.py", "TOOL-LOCAL/SystemSetup/piper-requirements.lock", "third_party/voice/PIPER_OFFLINE_APPROVED.json" })
        {
            var target = relative.StartsWith("third_party/", StringComparison.Ordinal)
                ? Path.Combine(publish, relative) : Path.Combine(publish, "workers", Path.GetFileName(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(FindRepositoryRoot(), relative), target);
        }
        return publish;
    }

    private async Task<(int ExitCode, string Output, string Error)> ValidateAsync(string settings, string mode, string? trust = null,
        bool includeQwen = false, string? qwenPath = null, string? piperPath = null, string? publishRoot = null)
    {
        var runner = Path.Combine(_root, "run-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(runner, """
            param($Validator,$Settings,$Mode,$Trust,$Include,$Qwen,$Piper,$Publish)
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
            $arguments = @{ SettingsPath=$Settings; ModelDeliveryMode=$Mode }
            if ($Trust -ne 'unused') { $arguments.TrustRootPath=$Trust }
            if ($Include -eq 'true') { $arguments.IncludeQwenModel=$true }
            if ($Qwen -ne 'unused') { $arguments.QwenModelPath=$Qwen }
            if ($Piper -ne 'unused') { $arguments.PiperBundlePath=$Piper }
            if ($Publish -ne 'unused') { $arguments.PublishRoot=$Publish }
            & $Validator @arguments | ConvertTo-Json -Compress
            """, new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner,
            "-Validator", Path.Combine(FindRepositoryRoot(), "scripts", "Test-ModelDistributionPublishSettings.ps1"),
            "-Settings", settings, "-Mode", mode, "-Trust", trust ?? "unused", "-Include", includeQwen ? "true" : "false",
            "-Qwen", qwenPath ?? "unused", "-Piper", piperPath ?? "unused", "-Publish", publishRoot ?? "unused" }) start.ArgumentList.Add(argument);
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
        throw new InvalidOperationException("Model publishing tests require the source checkout.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
