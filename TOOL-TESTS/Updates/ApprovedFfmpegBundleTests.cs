using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace TOOL_TESTS.Updates;

// File fixtures exercise the exact pin/containment checks used by the production
// preparation script. They never execute a native file or qualify a GPL release.
public sealed class ApprovedFfmpegBundleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-approved-ffmpeg-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_msbuild_hook_adds_all_first_publish_notices_without_rewriting_existing_content(bool designTime)
    {
        Directory.CreateDirectory(_root);
        var bundle = Path.Combine(_root, "first publish bundle");
        var helper = Path.Combine(_root, "controlled prepare fixture.ps1");
        await File.WriteAllTextAsync(helper, """
            param([string]$DestinationDirectory)
            $ErrorActionPreference = 'Stop'
            New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
            foreach ($name in @('ffmpeg.exe','ffprobe.exe','LICENSE.txt','PROVENANCE.md','checksums.sha256','README.gyan.txt','SOURCE-AVAILABILITY.md','TECHNICAL-PROBE.json')) {
                [System.IO.File]::WriteAllText((Join-Path $DestinationDirectory $name), 'controlled file fixture; never execute')
            }
            """);
        var properties = new Dictionary<string, string>
        {
            ["DesktopVisualStudioPackageEnabled"] = "true", ["DesktopPrepareApprovedFfmpegBundle"] = "true",
            ["DesktopVisualStudioFfmpegScript"] = helper, ["FfmpegBundleDirectory"] = bundle
        };
        var project = new XDocument(new XElement("Project",
            new XElement("PropertyGroup", properties.Select(x => new XElement(x.Key, x.Value))),
            new XElement("ItemGroup",
                new XElement("Content", new XAttribute("Include", "original-config.json"), new XAttribute("Link", "appsettings.json")),
                new XElement("Content", new XAttribute("Include", "original-worker.py"), new XAttribute("Link", "workers/piper_worker.py"))),
            new XElement("Import", new XAttribute("Project", Path.Combine(FindRepository(), "TOOL-LOCAL", "Build", "VisualStudioRelease.targets")))));
        var path = Path.Combine(_root, "fixture.proj");
        project.Save(path);
        var result = await RunProcessAsync("dotnet", ["msbuild", path, "-nologo", "-target:PrepareVisualStudioFfmpegBundle", "-getItem:Content", "-verbosity:quiet", "-p:DesignTimeBuild=" + designTime.ToString().ToLowerInvariant()]);
        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var content = document.RootElement.GetProperty("Items").GetProperty("Content").EnumerateArray().ToArray();
        Assert.Equal(designTime ? 2 : 10, content.Length);
        Assert.Single(content, x => x.GetProperty("Link").GetString() == "appsettings.json");
        Assert.Single(content, x => x.GetProperty("Link").GetString() == "workers/piper_worker.py");
        if (designTime) Assert.False(Directory.Exists(bundle));
        else
        {
            var files = content.Where(x => x.GetProperty("Link").GetString()!.StartsWith("tools\\ffmpeg\\")).ToArray();
            Assert.Equal(8, files.Length);
            Assert.Equal(8, files.Select(x => x.GetProperty("Link").GetString()).Distinct().Count());
            Assert.Contains(files, x => x.GetProperty("Link").GetString() == "tools\\ffmpeg\\SOURCE-AVAILABILITY.md");
            Assert.All(files, x => Assert.Equal("true", x.GetProperty("ExcludeFromSingleFile").GetString()));
        }
    }

    [Fact]
    public async Task Preparation_promotes_once_and_reuses_the_verified_bundle_without_requiring_source_binaries()
    {
        var prepare = CreatePreparationFixture();
        var body = $"& '{Escape(prepare)}' -SourceDirectory $Binary -DestinationDirectory $Output | ConvertTo-Json -Compress";
        var first = await RunAsync(body);
        Assert.True(first.ExitCode == 0, first.Error);
        using (var document = JsonDocument.Parse(first.Output)) Assert.False(document.RootElement.GetProperty("Reused").GetBoolean());
        var before = Directory.GetFiles(Path.Combine(_root, "output")).ToDictionary(x => Path.GetFileName(x)!, Hash);
        File.Delete(Path.Combine(_root, "binary", "ffmpeg.exe"));
        var second = await RunAsync(body);
        Assert.True(second.ExitCode == 0, second.Error);
        using (var document = JsonDocument.Parse(second.Output))
        {
            Assert.True(document.RootElement.GetProperty("Reused").GetBoolean());
            Assert.False(document.RootElement.GetProperty("ProductionQualified").GetBoolean());
        }
        Assert.Equal(before.OrderBy(x => x.Key), Directory.GetFiles(Path.Combine(_root, "output")).ToDictionary(x => Path.GetFileName(x)!, Hash).OrderBy(x => x.Key));
    }

    [Fact]
    public async Task Native_validation_failure_does_not_promote_or_leave_a_partial_bundle()
    {
        var prepare = CreatePreparationFixture(failNativeValidation: true);
        var result = await RunAsync($"& '{Escape(prepare)}' -SourceDirectory $Binary -DestinationDirectory $Output");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("controlled native validation failure", result.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        Assert.Empty(Directory.GetDirectories(_root, ".ffmpeg-release-*.part"));
        Assert.Equal("synthetic ffmpeg.exe; never execute", await File.ReadAllTextAsync(Path.Combine(_root, "binary", "ffmpeg.exe")));
    }

    [Fact]
    public async Task Preparation_rejects_an_altered_existing_notice_and_preserves_it_for_inspection()
    {
        var prepare = CreatePreparationFixture();
        var body = $"& '{Escape(prepare)}' -SourceDirectory $Binary -DestinationDirectory $Output";
        var first = await RunAsync(body);
        Assert.True(first.ExitCode == 0, first.Error);
        var notice = Path.Combine(_root, "output", "SOURCE-AVAILABILITY.md");
        await File.AppendAllTextAsync(notice, "altered cached notice");
        var before = Hash(notice);
        var second = await RunAsync(body);
        Assert.NotEqual(0, second.ExitCode);
        Assert.Contains("never overwritten", second.Error);
        Assert.Equal(before, Hash(notice));
    }

    [Fact]
    public async Task Technical_release_keeps_incomplete_source_and_redistribution_status_explicit()
    {
        CreateFixture();
        var result = await RunAsync("Get-ApprovedFfmpegDefinition $DefinitionPath | ConvertTo-Json -Depth 10 -Compress");
        Assert.True(result.ExitCode == 0, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var definition = document.RootElement;
        Assert.Equal("Release", definition.GetProperty("approvalScope").GetString());
        Assert.Equal("TechnicalPackaging", definition.GetProperty("approvalKind").GetString());
        Assert.False(definition.GetProperty("correspondingSourceComplete").GetBoolean());
        Assert.False(definition.GetProperty("redistributionQualified").GetBoolean());
        Assert.False(definition.GetProperty("productionQualified").GetBoolean());
    }

    [Theory]
    [InlineData("traversal-output")]
    [InlineData("traversal-source")]
    [InlineData("rooted-source")]
    [InlineData("duplicate-output")]
    [InlineData("device-output")]
    [InlineData("unapproved-binary")]
    [InlineData("missing-source-notice")]
    [InlineData("implicit-qualification")]
    public async Task Unsafe_or_incomplete_definitions_fail_before_any_output_is_created(string mutation)
    {
        CreateFixture();
        var path = Path.Combine(_root, "review", "approved.json");
        var definition = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var files = definition["files"]!.AsArray();
        switch (mutation)
        {
            case "traversal-output": files[0]!["path"] = "../escape.exe"; break;
            case "traversal-source": files[0]!["sourcePath"] = "../escape.exe"; break;
            case "rooted-source": files[0]!["sourcePath"] = Path.Combine(_root, "outside.exe"); break;
            case "duplicate-output": files[1]!["path"] = "FFMPEG.EXE"; break;
            case "device-output": files[0]!["path"] = "CON.txt"; break;
            case "unapproved-binary": files[0]!["sourcePath"] = "surprise.exe"; break;
            case "missing-source-notice": files.RemoveAt(files.Count - 1); break;
            case "implicit-qualification": definition.Remove("productionQualified"); break;
        }
        await File.WriteAllTextAsync(path, definition.ToJsonString());
        var result = await RunAsync("Get-ApprovedFfmpegDefinition $DefinitionPath");
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
        Assert.Equal("synthetic ffmpeg.exe; never execute", await File.ReadAllTextAsync(Path.Combine(_root, "binary", "ffmpeg.exe")));
    }

    [Fact]
    public async Task Missing_review_does_not_create_or_relabel_a_development_bundle()
    {
        CreateFixture();
        File.Delete(Path.Combine(_root, "review", "approved.json"));
        var result = await RunAsync("Get-ApprovedFfmpegDefinition $DefinitionPath");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("do not relabel", result.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
    }

    [Theory]
    [InlineData("ffmpeg.exe")]
    [InlineData("SOURCE-AVAILABILITY.md")]
    public async Task Changed_binary_or_review_source_is_rejected_before_copying(string changed)
    {
        CreateFixture();
        var path = changed == "ffmpeg.exe" ? Path.Combine(_root, "binary", changed) : Path.Combine(_root, "review", "documents", changed);
        await File.AppendAllTextAsync(path, "tampered");
        var result = await RunAsync("$d = Get-ApprovedFfmpegDefinition $DefinitionPath; Get-ApprovedFfmpegSourceFiles $Binary $Review $d");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SHA-256 changed", result.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "output")));
    }

    [Fact]
    public async Task Valid_source_plan_covers_native_files_license_and_every_review_notice()
    {
        CreateFixture();
        var result = await RunAsync("$d = Get-ApprovedFfmpegDefinition $DefinitionPath; @(Get-ApprovedFfmpegSourceFiles $Binary $Review $d) | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(6, document.RootElement.GetArrayLength());
        Assert.Contains(document.RootElement.EnumerateArray(), x => x.GetProperty("Name").GetString() == "SOURCE-AVAILABILITY.md");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("binary")]
    [InlineData("notice")]
    [InlineData("extra")]
    [InlineData("missing")]
    public async Task Reuse_requires_exact_pinned_files_and_never_overwrites_a_changed_bundle(string mutation)
    {
        CreateFixture();
        var output = Path.Combine(_root, "output");
        Directory.CreateDirectory(output);
        foreach (var file in Directory.GetFiles(Path.Combine(_root, "binary"))) File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
        foreach (var file in Directory.GetFiles(Path.Combine(_root, "review", "documents"))) File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
        var binary = Path.Combine(output, "ffmpeg.exe");
        switch (mutation)
        {
            case "binary": await File.AppendAllTextAsync(binary, "tampered"); break;
            case "notice": await File.AppendAllTextAsync(Path.Combine(output, "SOURCE-AVAILABILITY.md"), "tampered"); break;
            case "extra": await File.WriteAllTextAsync(Path.Combine(output, "stale.exe"), "unexpected"); break;
            case "missing": File.Delete(Path.Combine(output, "LICENSE.txt")); break;
        }
        var before = await File.ReadAllBytesAsync(binary);
        var result = await RunAsync("$d = Get-ApprovedFfmpegDefinition $DefinitionPath; Test-ApprovedFfmpegFiles $Output $d");
        Assert.Equal(mutation == "none", result.ExitCode == 0);
        Assert.Equal(before, await File.ReadAllBytesAsync(binary));
        if (mutation != "none") Assert.Contains("never overwritten", result.Error);
    }

    private void CreateFixture()
    {
        var binary = Path.Combine(_root, "binary");
        var review = Path.Combine(_root, "review");
        var documents = Path.Combine(review, "documents");
        Directory.CreateDirectory(binary);
        Directory.CreateDirectory(documents);
        var files = new List<object>();
        foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe", "LICENSE.txt" })
        {
            var path = Path.Combine(binary, name);
            File.WriteAllText(path, "synthetic " + name + "; never execute");
            files.Add(new { path = name, source = "binary", sourcePath = name, sha256 = Hash(path) });
        }
        foreach (var name in new[] { "PROVENANCE.md", "checksums.sha256", "SOURCE-AVAILABILITY.md" })
        {
            var path = Path.Combine(documents, name);
            File.WriteAllText(path, "synthetic reviewed " + name);
            files.Add(new { path = name, source = "review", sourcePath = "documents/" + name, sha256 = Hash(path) });
        }
        File.WriteAllText(Path.Combine(review, "approved.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, bundleId = "synthetic-test-only", version = "1.0-test", architecture = "win-x64",
            approvalScope = "Release", approvalKind = "TechnicalPackaging", correspondingSourceComplete = false,
            redistributionQualified = false, productionQualified = false, files
        }));
    }

    private string CreatePreparationFixture(bool failNativeValidation = false)
    {
        CreateFixture();
        var repository = FindRepository();
        var mirror = Path.Combine(_root, "controlled script mirror");
        var scripts = Path.Combine(mirror, "scripts");
        var review = Path.Combine(mirror, "third_party", "ffmpeg");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(Path.Combine(review, "documents"));
        foreach (var name in new[] { "Prepare-ReleaseFfmpegBundle.ps1", "ApprovedFfmpegBundle.psm1" })
            File.Copy(Path.Combine(repository, "scripts", name), Path.Combine(scripts, name));
        foreach (var file in Directory.GetFiles(Path.Combine(_root, "review", "documents")))
            File.Copy(file, Path.Combine(review, "documents", Path.GetFileName(file)));
        var definition = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "review", "approved.json")))!;
        definition["bundleId"] = "gyan-9.0.1-essentials-win-x64";
        definition["version"] = "9.0.1-essentials_build-www.gyan.dev";
        File.WriteAllText(Path.Combine(review, "FFMPEG_RELEASE_APPROVED.json"), definition.ToJsonString());
        // Only this isolated mirror replaces native validation. The production
        // script and module are copied unchanged; no synthetic EXE is executed.
        File.WriteAllText(Path.Combine(scripts, "Test-FfmpegBundle.ps1"), $$"""
            param([string]$BundlePath, [string]$ExpectedVersion, [switch]$RequireReleaseApproval)
            if (-not $RequireReleaseApproval) { throw 'Release validation was omitted' }
            {{(failNativeValidation ? "throw 'controlled native validation failure'" : "[pscustomobject]@{ Version = $ExpectedVersion }")}}
            """);
        return Path.Combine(scripts, "Prepare-ReleaseFfmpegBundle.ps1");
    }

    private async Task<ProcessResult> RunAsync(string body)
    {
        var module = Path.Combine(FindRepository(), "scripts", "ApprovedFfmpegBundle.psm1");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module '{{Escape(module)}}' -Force
            $DefinitionPath = '{{Escape(Path.Combine(_root, "review", "approved.json"))}}'
            $Binary = '{{Escape(Path.Combine(_root, "binary"))}}'
            $Review = '{{Escape(Path.Combine(_root, "review"))}}'
            $Output = '{{Escape(Path.Combine(_root, "output"))}}'
            {{body}}
            """;
        return await RunProcessAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]);
    }

    private static async Task<ProcessResult> RunProcessAsync(string executable, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
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
            if (File.Exists(Path.Combine(current.FullName, "scripts", "ApprovedFfmpegBundle.psm1"))) return current.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
