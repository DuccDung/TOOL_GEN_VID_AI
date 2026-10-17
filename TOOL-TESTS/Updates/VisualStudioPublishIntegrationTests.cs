using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace TOOL_TESTS.Updates;

// Executes MSBuild evaluation, an isolated target harness and a small SDK console
// publish, never a customer application publish or a native/model probe.
// The actual production .targets file is imported; only its two script entrypoints are
// replaced by local trace stubs. No customer configuration, application execution,
// SQL connection, model download or provider request is used.
public sealed class VisualStudioPublishIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-vs-publish-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Actual_sdk_single_file_publish_keeps_linked_runtime_content_loose_and_managed_references_bundled()
    {
        var fixture = await CreateFixtureAsync();
        var project = XDocument.Load(fixture.Project);
        project.Root!.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");
        project.Root.Elements("Target").Remove();
        project.Root.Elements("ItemGroup").Remove();
        var properties = project.Root.Element("PropertyGroup")!;
        void Set(string name, string value)
        {
            var element = properties.Element(name);
            if (element is null) properties.Add(new XElement(name, value));
            else element.Value = value;
        }
        Set("TargetFramework", "net10.0");
        Set("OutputType", "Exe");
        Set("AssemblyName", "TOOL-LOCAL");
        Set("SelfContained", "false");
        Set("PublishReadyToRun", "false");
        Set("EnableDefaultCompileItems", "false");
        Set("DebugType", "None");
        Set("DebugSymbols", "false");
        Set("OutputPath", Path.Combine(_root, "sdk-bin") + Path.DirectorySeparatorChar);
        var program = Path.Combine(_root, "Program.cs");
        await File.WriteAllTextAsync(program, "System.Console.WriteLine(\"controlled SDK fixture; never executed\");");
        var content = new XElement("ItemGroup", new XElement("Compile", new XAttribute("Include", program)));
        var runtimeFiles = new Dictionary<string, string>
        {
            ["workers/piper_worker.py"] = "PreserveNewest", ["workers/piper-requirements.lock"] = "Always",
            ["setup-fixtures/ocr.png"] = "PreserveNewest", ["third_party/voice/LICENSES.md"] = "IfDifferent"
        };
        var external = Path.Combine(_root, "linked runtime inputs");
        Directory.CreateDirectory(external);
        foreach (var (link, copy) in runtimeFiles)
        {
            var path = Path.Combine(external, Path.GetFileName(link));
            await File.WriteAllTextAsync(path, "controlled runtime fixture " + link);
            content.Add(new XElement("Content", new XAttribute("Include", path),
                new XAttribute("Link", link), new XAttribute("CopyToOutputDirectory", "PreserveNewest"),
                new XAttribute("CopyToPublishDirectory", copy)));
        }
        var privateInput = Path.Combine(external, "private.txt");
        await File.WriteAllTextAsync(privateInput, "synthetic unpublished fixture");
        content.Add(new XElement("Content", new XAttribute("Include", privateInput),
            new XAttribute("Link", "private/config.txt"), new XAttribute("CopyToOutputDirectory", "Never"),
            new XAttribute("CopyToPublishDirectory", "Never")));
        var managedReference = typeof(Assert).Assembly.Location;
        content.Add(new XElement("Reference", new XAttribute("Include", "xunit.assert"),
            new XElement("HintPath", managedReference), new XElement("Private", "true")));
        project.Root.Add(content);
        // Only prerequisites are stubbed: the SDK bundle selection, copy targets,
        // production loose-content hook and publish file-list hook run unchanged.
        project.Root.Add(new XElement("Target", new XAttribute("Name", "ValidateVisualStudioDesktopRelease"),
            new XElement("Exec", new XAttribute("Command", "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"$(DesktopVisualStudioPreflightScript)\""))));
        var bundleList = Path.Combine(_root, "sdk-bundled-files.txt");
        var contentMetadata = Path.Combine(_root, "sdk-content-metadata.txt");
        project.Root.Add(new XElement("Target", new XAttribute("Name", "RecordControlledSdkBundleInputs"),
            new XAttribute("AfterTargets", "_ComputeFilesToBundle"),
            new XElement("WriteLinesToFile", new XAttribute("File", bundleList),
                new XAttribute("Lines", "@(_FilesToBundle->'%(RelativePath)')"), new XAttribute("Overwrite", "true")),
            new XElement("WriteLinesToFile", new XAttribute("File", contentMetadata),
                new XAttribute("Lines", "@(Content->'%(Link)|%(CopyToPublishDirectory)|%(ExcludeFromSingleFile)')"), new XAttribute("Overwrite", "true"))));
        // dotnet publish accepts an SDK project; .proj is retained for the old
        // plain harness, so use a separate .csproj for the real SDK fixture.
        var sdkProject = Path.Combine(_root, "RuntimeContentFixture.csproj");
        await File.WriteAllTextAsync(sdkProject, project.ToString(), new UTF8Encoding(false));
        var result = await RunAsync(["publish", sdkProject, "-nologo", "-v:quiet", "-c", "Release", "--ignore-failed-sources"], fixture);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        var package = ReadScriptCalls(fixture).Single(x => x.Stage == "package");
        var arguments = package.Arguments.ToArray();
        var listIndex = Array.IndexOf(arguments, "-PublishFileListPath");
        Assert.InRange(listIndex, 0, arguments.Length - 2);
        var published = await File.ReadAllLinesAsync(arguments[listIndex + 1]);
        foreach (var link in runtimeFiles.Keys)
        {
            Assert.Contains(link, published);
            Assert.Equal("controlled runtime fixture " + link, await File.ReadAllTextAsync(Path.Combine(fixture.PublishRoot, link)));
        }
        var bundled = await File.ReadAllLinesAsync(bundleList);
        Assert.Contains("TOOL-LOCAL.dll", bundled);
        Assert.Contains(Path.GetFileName(managedReference), bundled);
        Assert.DoesNotContain("TOOL-LOCAL.dll", published);
        Assert.DoesNotContain(Path.GetFileName(managedReference), published);
        Assert.False(File.Exists(Path.Combine(fixture.PublishRoot, Path.GetFileName(managedReference))));
        Assert.Contains("private/config.txt|Never|", await File.ReadAllLinesAsync(contentMetadata));
        Assert.DoesNotContain("private/config.txt", published);
        Assert.False(File.Exists(Path.Combine(fixture.PublishRoot, "private", "config.txt")));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("profile-name")]
    [InlineData("visual-studio-profile-file")]
    public async Task Actual_profile_selects_compiled_shared_identity_without_changing_normal_build_defaults(string selection)
    {
        var repository = FindRepository();
        var projectPath = Path.Combine(repository, "TOOL-LOCAL", "TOOL-LOCAL.csproj");
        var profile = selection != "none";
        var arguments = new List<string>
        {
            "msbuild", projectPath, "-nologo",
            "-p:Version=1.9.2", "-p:DesktopBuildNumber=37",
            "-getProperty:DesktopVisualStudioPackageEnabled,DesktopCreateVerifiedReleaseZip,DesktopCreateCanonicalReleaseZip,DesktopDistributionProtocol,DesktopReleaseProfile,StaticModelSetupRequired,StaticModelReleaseChannel,IncludePiperOfflinePayload,RequirePiperOfflineBundle,InformationalVersion,IncludeSourceRevisionInInformationalVersion",
            "-getItem:AssemblyAttribute"
        };
        if (selection == "profile-name") arguments.Add("-p:PublishProfile=FolderProfile");
        else if (selection == "visual-studio-profile-file")
        {
            arguments.Add("-p:WebPublishProfileFile=" + Path.Combine(repository, "TOOL-LOCAL", "Properties", "PublishProfiles", "FolderProfile.pubxml"));
            arguments.Add("-p:ProjectToOverrideProjectExtensionsPath=" + projectPath);
        }
        var result = await RunAsync(arguments);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var properties = document.RootElement.GetProperty("Properties");
        var metadata = document.RootElement.GetProperty("Items").GetProperty("AssemblyAttribute")
            .EnumerateArray().Where(x => x.GetProperty("Identity").GetString() == "System.Reflection.AssemblyMetadataAttribute")
            .ToDictionary(x => x.GetProperty("_Parameter1").GetString()!, x => x.GetProperty("_Parameter2").GetString());
        Assert.Equal(profile ? "3" : "2", properties.GetProperty("DesktopDistributionProtocol").GetString());
        Assert.Equal(profile ? "3" : "2", metadata["DesktopDistributionProtocol"]);
        Assert.Equal(profile ? "true" : "false", properties.GetProperty("StaticModelSetupRequired").GetString());
        Assert.Equal(profile ? "true" : "false", metadata["StaticModelSetupRequired"]);
        Assert.Equal("37", metadata["DesktopBuildNumber"]);
        Assert.Equal(properties.GetProperty("DesktopReleaseProfile").GetString(), metadata["DesktopReleaseProfile"]);
        Assert.Equal("Stable", metadata["StaticModelReleaseChannel"]);
        Assert.Equal(profile ? "false" : "true", properties.GetProperty("IncludePiperOfflinePayload").GetString());
        if (profile)
        {
            Assert.Equal("true", properties.GetProperty("DesktopVisualStudioPackageEnabled").GetString());
            Assert.Equal("false", properties.GetProperty("DesktopCreateVerifiedReleaseZip").GetString());
            Assert.Equal("true", properties.GetProperty("DesktopCreateCanonicalReleaseZip").GetString());
            Assert.Equal("false", properties.GetProperty("RequirePiperOfflineBundle").GetString());
            Assert.Equal("1.9.2+desktopbuild.37", properties.GetProperty("InformationalVersion").GetString());
            Assert.Equal("false", properties.GetProperty("IncludeSourceRevisionInInformationalVersion").GetString());
        }
        else Assert.NotEqual("true", properties.GetProperty("DesktopVisualStudioPackageEnabled").GetString());
    }

    [Fact]
    public async Task Plain_publish_finishes_without_starting_optional_metadata_zip_packaging()
    {
        var fixture = await CreateFixtureAsync();
        var result = await RunFixtureAsync(fixture, "Publish", "DesktopCreateVerifiedReleaseZip=false", failureStage: "package");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Equal(new[] { "preflight", "BUILD", "PUBLISH" }, ReadStages(fixture));
        Assert.Equal("preflight", Assert.Single(ReadScriptCalls(fixture)).Stage);
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public async Task Canonical_default_target_uses_the_current_SDK_file_list_and_never_starts_rich_metadata_packaging()
    {
        var fixture = await CreateFixtureAsync();
        var canonical = Path.Combine(_root, "canonical fixture.ps1");
        await File.WriteAllTextAsync(canonical, Stub.Replace("__STAGE__", "canonical"), new UTF8Encoding(true));
        var project = XDocument.Load(fixture.Project);
        var properties = project.Root!.Element("PropertyGroup")!;
        properties.Add(new XElement("DesktopCreateVerifiedReleaseZip", "false"), new XElement("DesktopCreateCanonicalReleaseZip", "true"),
            new XElement("DesktopVisualStudioCanonicalPackageScript", canonical), new XElement("DesktopCanonicalPackageOutputRoot", fixture.OutputRoot));
        await File.WriteAllTextAsync(fixture.Project, project.ToString(), new UTF8Encoding(false));
        var result = await RunFixtureAsync(fixture, "Publish", failureStage: "package");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Equal(new[] { "preflight", "BUILD", "PUBLISH", "canonical" }, ReadStages(fixture));
        var call = ReadScriptCalls(fixture).Single(x => x.Stage == "canonical");
        var index = call.Arguments.ToList().IndexOf("-PublishFileListPath");
        Assert.True(index >= 0);
        Assert.Equal(new[] { "TOOL-LOCAL.exe", "tools/fixture.dat" }, await File.ReadAllLinesAsync(call.Arguments[index + 1]));
    }

    [Theory]
    [InlineData("Build", "DesktopVisualStudioPackageEnabled=false")]
    [InlineData("Publish", "DesktopVisualStudioPackageEnabled=false")]
    [InlineData("Publish", "DesignTimeBuild=true")]
    public async Task Build_disabled_profile_and_design_time_never_start_packaging_scripts(string target, string? property)
    {
        var fixture = await CreateFixtureAsync();
        var result = await RunFixtureAsync(fixture, target, property);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Empty(ReadScriptCalls(fixture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Folder_publish_runs_preflight_before_publish_and_packages_exactly_once_afterward(bool trailingSlash)
    {
        var fixture = await CreateFixtureAsync(trailingSlash: trailingSlash);
        var result = await RunFixtureAsync(fixture, "Publish");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Equal(new[] { "preflight", "BUILD", "PUBLISH", "package" }, ReadStages(fixture));
        var calls = ReadScriptCalls(fixture);
        Assert.Equal(2, calls.Count);
        Assert.Equal("preflight", calls[0].Stage);
        Assert.Equal("package", calls[1].Stage);
        Assert.Contains(fixture.Settings, calls[0].Arguments);
        Assert.Contains(fixture.Settings, calls[1].Arguments);
        Assert.Contains(fixture.WebViewInstaller, calls[0].Arguments);
        Assert.Contains(fixture.WebViewInstaller, calls[1].Arguments);
        Assert.Contains(calls[1].Arguments, value => SamePath(value, fixture.PublishRoot));
        Assert.Contains(calls[1].Arguments, value => SamePath(value, fixture.OutputRoot));
        Assert.Contains("1.9.2", calls[1].Arguments);
        Assert.Contains("37", calls[1].Arguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Single_file_apphost_is_listed_once_even_when_sdk_omits_it_from_resolved_files(bool sdkListsApphost)
    {
        var fixture = await CreateFixtureAsync(sdkListsApphost: sdkListsApphost);
        var result = await RunFixtureAsync(fixture, "Publish");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        var package = ReadScriptCalls(fixture).Single(x => x.Stage == "package");
        var arguments = package.Arguments.ToArray();
        var index = Array.IndexOf(arguments, "-PublishFileListPath");
        Assert.InRange(index, 0, arguments.Length - 2);
        Assert.True(Path.IsPathFullyQualified(arguments[index + 1]));
        var files = await File.ReadAllLinesAsync(arguments[index + 1]);
        Assert.Equal(1, files.Count(x => string.Equals(x, "TOOL-LOCAL.exe", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, files.Count(x => x == "tools/fixture.dat"));
        Assert.Equal(2, files.Length);
    }

    [Fact]
    public async Task Profile_build_only_runs_preflight_and_never_packages_build_output()
    {
        var fixture = await CreateFixtureAsync();
        var result = await RunFixtureAsync(fixture, "Build");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Equal(new[] { "preflight", "BUILD" }, ReadStages(fixture));
        Assert.Equal("preflight", Assert.Single(ReadScriptCalls(fixture)).Stage);
    }

    [Fact]
    public async Task No_build_publish_still_validates_inputs_and_packages_the_published_files()
    {
        var fixture = await CreateFixtureAsync();
        var result = await RunFixtureAsync(fixture, "Publish", "NoBuild=true");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Equal(new[] { "preflight", "PUBLISH", "package" }, ReadStages(fixture));
        Assert.Equal(new[] { "preflight", "package" }, ReadScriptCalls(fixture).Select(x => x.Stage));
    }

    [Theory]
    [InlineData("preflight")]
    [InlineData("package")]
    public async Task Script_failure_fails_publish_and_never_reports_a_successful_package(string stage)
    {
        var fixture = await CreateFixtureAsync();
        var result = await RunFixtureAsync(fixture, "Publish", failureStage: stage);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(stage == "preflight"
                ? new[] { "preflight" }
                : new[] { "preflight", "BUILD", "PUBLISH", "package" },
            ReadStages(fixture));
        Assert.Equal(stage == "preflight" ? 1 : 2, ReadScriptCalls(fixture).Count);
    }

    private async Task<Fixture> CreateFixtureAsync(bool trailingSlash = true, bool sdkListsApphost = true)
    {
        var input = Path.Combine(_root, "inputs & prerequisites");
        Directory.CreateDirectory(input);
        var fixture = new Fixture(Path.Combine(_root, "fixture.proj"), Path.Combine(_root, "trace.log"),
            Path.Combine(_root, "calls.jsonl"), Path.Combine(input, "release settings.json"),
            Path.Combine(input, "offline webview fixture.exe"), Path.Combine(_root, "published folder"),
            Path.Combine(_root, "release archives"));
        Directory.CreateDirectory(fixture.PublishRoot);
        Directory.CreateDirectory(Path.Combine(_root, "intermediate"));
        Directory.CreateDirectory(Path.Combine(fixture.PublishRoot, "tools"));
        await File.WriteAllTextAsync(fixture.Settings, "{}", new UTF8Encoding(false));
        await File.WriteAllBytesAsync(fixture.WebViewInstaller, new byte[] { 0 });
        await File.WriteAllBytesAsync(Path.Combine(fixture.PublishRoot, "TOOL-LOCAL.exe"), new byte[] { 0 });
        await File.WriteAllTextAsync(Path.Combine(fixture.PublishRoot, "tools", "fixture.dat"), "synthetic payload");
        var preflight = Path.Combine(input, "preflight fixture.ps1");
        var package = Path.Combine(input, "package fixture.ps1");
        await File.WriteAllTextAsync(preflight, Stub.Replace("__STAGE__", "preflight"), new UTF8Encoding(true));
        await File.WriteAllTextAsync(package, Stub.Replace("__STAGE__", "package"), new UTF8Encoding(true));
        var properties = new Dictionary<string, string>
        {
            ["DesktopVisualStudioPackageEnabled"] = "true", ["Configuration"] = "Release",
            ["Version"] = "1.9.2", ["DesktopBuildNumber"] = "37", ["TargetFramework"] = "net10.0-windows",
            ["RuntimeIdentifier"] = "win-x64", ["SelfContained"] = "true", ["PublishSingleFile"] = "true",
            ["DesktopDistributionProtocol"] = "3", ["DesktopReleaseProfile"] = "Production",
            ["StaticModelSetupRequired"] = "true", ["StaticModelReleaseChannel"] = "Stable",
            ["IncludePiperOfflinePayload"] = "false", ["RequirePiperOfflineBundle"] = "false",
            ["InformationalVersion"] = "1.9.2+desktopbuild.37", ["IncludeSourceRevisionInInformationalVersion"] = "false",
            ["DesktopMinimumBootstrapperVersion"] = "1.0.0", ["PublishProtocol"] = "FileSystem",
            ["DesktopDeploymentSettingsPath"] = fixture.Settings, ["DesktopWebView2InstallerPath"] = fixture.WebViewInstaller,
            ["DesktopPackageOutputRoot"] = fixture.OutputRoot,
            ["PublishDir"] = fixture.PublishRoot + (trailingSlash ? Path.DirectorySeparatorChar.ToString() : ""),
            ["PublishedSingleFileName"] = "TOOL-LOCAL.exe", ["PublishedSingleFilePath"] = Path.Combine(fixture.PublishRoot, "TOOL-LOCAL.exe"),
            ["DesktopVisualStudioPowerShell"] = "powershell.exe", ["DesktopVisualStudioPreflightScript"] = preflight,
            ["DesktopVisualStudioPackageScript"] = package, ["TracePath"] = fixture.Trace,
            ["FfmpegBundleDirectory"] = input, ["OcrNativeRuntimeDirectory"] = input,
            ["IntermediateOutputPath"] = Path.Combine(_root, "intermediate") + Path.DirectorySeparatorChar
        };
        var project = new XDocument(new XElement("Project",
            new XElement("PropertyGroup", properties.Select(x => new XElement(x.Key, x.Value))),
            new XElement("ItemGroup",
                sdkListsApphost ? new XElement("ResolvedFileToPublish",
                    new XAttribute("Include", Path.Combine(fixture.PublishRoot, "TOOL-LOCAL.exe")),
                    new XElement("RelativePath", "TOOL-LOCAL.exe")) : null,
                new XElement("ResolvedFileToPublish", new XAttribute("Include", Path.Combine(fixture.PublishRoot, "tools", "fixture.dat")),
                    new XElement("RelativePath", "tools\\fixture.dat"))),
            new XElement("Import", new XAttribute("Project", Path.Combine(FindRepository(), "TOOL-LOCAL", "Build", "VisualStudioRelease.targets"))),
            new XElement("Target", new XAttribute("Name", "PrepareForBuild")),
            new XElement("Target", new XAttribute("Name", "Build"), new XAttribute("DependsOnTargets", "PrepareForBuild"),
                new XAttribute("Condition", "'$(NoBuild)' != 'true'"), Trace("BUILD")),
            new XElement("Target", new XAttribute("Name", "PrepareForPublish")),
            new XElement("Target", new XAttribute("Name", "Publish"), new XAttribute("DependsOnTargets", "Build;PrepareForPublish"), Trace("PUBLISH"))));
        await File.WriteAllTextAsync(fixture.Project, project.ToString(), new UTF8Encoding(false));
        return fixture;
    }

    private static XElement Trace(string value) => new("WriteLinesToFile", new XAttribute("File", "$(TracePath)"),
        new XAttribute("Lines", value), new XAttribute("Overwrite", "false"));

    private Task<ProcessResult> RunFixtureAsync(Fixture fixture, string target, string? property = null, string? failureStage = null)
    {
        var arguments = new List<string> { "msbuild", fixture.Project, "-nologo", "-v:minimal", "-t:" + target };
        if (property is not null) arguments.Add("-p:" + property);
        return RunAsync(arguments, fixture, failureStage);
    }

    private static async Task<ProcessResult> RunAsync(IEnumerable<string> arguments, Fixture? fixture = null, string? failureStage = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = FindRepository()
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (fixture is not null)
        {
            start.Environment["VM_VS_TRACE"] = fixture.Trace;
            start.Environment["VM_VS_CALLS"] = fixture.Calls;
            start.Environment["VM_VS_FAIL"] = failureStage ?? "";
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("MSBuild did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static IReadOnlyList<ScriptCall> ReadScriptCalls(Fixture fixture)
    {
        if (!File.Exists(fixture.Calls)) return Array.Empty<ScriptCall>();
        return File.ReadAllLines(fixture.Calls).Where(x => !string.IsNullOrWhiteSpace(x)).Select(line =>
        {
            using var document = JsonDocument.Parse(line.TrimStart('\uFEFF'));
            var value = document.RootElement;
            return new ScriptCall(value.GetProperty("stage").GetString()!, value.GetProperty("arguments")
                .EnumerateArray().Select(x => x.GetString()!).ToArray());
        }).ToArray();
    }

    private static string[] ReadStages(Fixture fixture) => File.Exists(fixture.Trace)
        ? File.ReadAllLines(fixture.Trace).Select(x => x.TrimStart('\uFEFF')).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
        : Array.Empty<string>();

    private static bool SamePath(string candidate, string expected) => Path.IsPathFullyQualified(candidate)
        && string.Equals(Path.GetFullPath(candidate).TrimEnd('\\', '/'), Path.GetFullPath(expected).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Source checkout is required for MSBuild integration tests.");
    }

    public void Dispose()
    {
        var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_root).StartsWith(temporary, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture root.");
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record Fixture(string Project, string Trace, string Calls, string Settings, string WebViewInstaller, string PublishRoot, string OutputRoot);
    private sealed record ProcessResult(int ExitCode, string Output, string Error);
    private sealed record ScriptCall(string Stage, IReadOnlyList<string> Arguments);

    private const string Stub = """
        $ErrorActionPreference = 'Stop'
        $stage = '__STAGE__'
        [IO.File]::AppendAllText($env:VM_VS_TRACE, $stage + [Environment]::NewLine)
        $call = [ordered]@{stage=$stage;arguments=@($args | ForEach-Object {[string]$_})}
        [IO.File]::AppendAllText($env:VM_VS_CALLS, ($call | ConvertTo-Json -Depth 4 -Compress) + [Environment]::NewLine)
        if ($env:VM_VS_FAIL -eq $stage) {exit 27}
        exit 0
        """;
}
