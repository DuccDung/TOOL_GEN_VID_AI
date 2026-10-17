using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TOOL_TESTS.Updates;

public sealed class ServerPackageSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-srv-safe-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SanitizerClearsSecretsAndSmtpUserIncludingArraysAndPreservesPublicDefaults()
    {
        var result = await RunHelperAsync("clean");
        Assert.True(result.ExitCode == 0, result.Error);
        var settingsText = await File.ReadAllTextAsync(Path.Combine(_root, "publish", "appsettings.json"));
        Assert.DoesNotContain("fixture-secret", settingsText + result.Output + result.Error);
        using var settings = JsonDocument.Parse(settingsText);
        var value = settings.RootElement;
        Assert.Equal("", value.GetProperty("ConnectionStrings").GetProperty("VideoFactory").GetString());
        Assert.Equal("", value.GetProperty("Smtp").GetProperty("User").GetString());
        Assert.Equal("smtp.fixture.invalid", value.GetProperty("Smtp").GetProperty("Host").GetString());
        Assert.Equal(587, value.GetProperty("Smtp").GetProperty("Port").GetInt32());
        Assert.Equal(15, value.GetProperty("Jwt").GetProperty("AccessTokenMinutes").GetInt32());
        Assert.Equal("https://public.fixture.invalid/", value.GetProperty("PublicBaseUrl").GetString());
        Assert.Equal("*", value.GetProperty("AllowedHosts").GetString());
        Assert.False(value.GetProperty("ModelDistribution").GetProperty("Enabled").GetBoolean());
        Assert.Empty(value.GetProperty("ModelDistribution").GetProperty("PublicKeysPem").EnumerateObject());
        Assert.Empty(value.GetProperty("ModelDistribution").GetProperty("TrustedCatalogSha256ByChannel").EnumerateObject());
        Assert.True(File.Exists(Path.Combine(_root, "archive.marker")));
        Assert.Equal("third party license", await File.ReadAllTextAsync(Path.Combine(_root, "publish", "runtime", "LICENSE.txt")));
    }

    [Theory]
    [InlineData("artifacts/debug-build/appsettings.json")]
    [InlineData("assets/appsettings.Debug.json")]
    [InlineData("assets/devsettings.json")]
    [InlineData("App_Data/workspace.json")]
    [InlineData("output/report.json")]
    [InlineData(".tmp/report.json")]
    [InlineData(".vs/options.json")]
    [InlineData("App_ModelDistribution/metadata/Staging/catalog.json")]
    [InlineData("secrets/account.json")]
    [InlineData("assets/signing.pfx")]
    [InlineData("assets/private.key")]
    [InlineData("assets/.env")]
    [InlineData("assets/launchSettings.json")]
    public async Task UnsafeNestedFilesFailBeforeArchiveAndDoNotLogTheirContents(string relativePath)
    {
        var result = await RunHelperAsync("unsafe-path", relativePath);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_root, "archive.marker")));
        Assert.DoesNotContain("fixture-secret", result.Output + result.Error);
    }

    [Theory]
    [InlineData("private-material")]
    [InlineData("web-config-override")]
    [InlineData("unsanitized")]
    [InlineData("enabled")]
    [InlineData("trust-reintroduced")]
    [InlineData("malformed")]
    public async Task FinalAuditRejectsPrivateMaterialOverridesAndSettingsTampering(string mutation)
    {
        var result = await RunHelperAsync(mutation);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_root, "archive.marker")));
        Assert.DoesNotContain("fixture-secret", result.Output + result.Error);
    }

    [Fact]
    public void PublishScriptAuditsBeforeArchiveAndReturnsOutputForWrapper()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "Publish-ToolServerIis.ps1"));
        var sanitize = script.IndexOf("Set-ServerPackageSafeSettings -PublishDirectory $publishDir", StringComparison.Ordinal);
        var audit = script.LastIndexOf("Assert-ServerPackageSafe -PublishDirectory $publishDir", StringComparison.Ordinal);
        var archive = script.IndexOf("Compress-Archive", StringComparison.Ordinal);
        Assert.True(sanitize >= 0 && audit > sanitize && archive > audit);
        Assert.Contains("[switch]$SkipArchive", script);
        Assert.Contains("PublishDirectory = $publishDir", script);
        Assert.Contains("ServerDllSha256 = $serverDllSha256", script);
        var project = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "TOOL-SERVER", "TOOL-SERVER.csproj"));
        Assert.Contains("<Content Remove=\"$(ServerPrivateContent)\"", project);
        foreach (var path in new[] { "artifacts/**", "output/**", ".tmp/**", ".vs/**", "App_Data/**", "App_ModelDistribution/**" })
            Assert.Contains(path, project);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunHelperAsync(string mutation, string relativePath = "")
    {
        Directory.CreateDirectory(_root);
        var runner = Path.Combine(_root, "run.ps1");
        await File.WriteAllTextAsync(runner, """
            param($ModulePath,$Root,$Mutation,$RelativePath)
            $ErrorActionPreference='Stop'
            [Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
            Import-Module $ModulePath -Force
            $publish=Join-Path $Root 'publish'
            [IO.Directory]::CreateDirectory((Join-Path $publish 'runtime'))|Out-Null
            $settingsPath=Join-Path $publish 'appsettings.json'
            $fixture=@{
                ConnectionStrings=@{VideoFactory='fixture-secret-sql'}
                Smtp=@{User='fixture-secret-account';Password='fixture-secret-password';Host='smtp.fixture.invalid';Port=587}
                Jwt=@{SigningKey='fixture-secret-signing';AccessTokenMinutes=15}
                Admin=@{BootstrapEmail='fixture-secret-admin'}
                Tokens=@('fixture-secret-token',@{Value='fixture-secret-nested'})
                PublicBaseUrl='https://public.fixture.invalid/';AllowedHosts='*'
                ModelDistribution=@{Enabled=$true;PublicKeysPem=@{fixture='public-not-secret'};TrustedCatalogSha256ByChannel=@{Staging=('a'*64)}}
            }
            [IO.File]::WriteAllText($settingsPath,($fixture|ConvertTo-Json -Depth 20))
            [IO.File]::WriteAllText((Join-Path $publish 'runtime/LICENSE.txt'),'third party license')
            [IO.File]::WriteAllText((Join-Path $publish 'TOOL-SERVER.deps.json'),'{}')
            Set-ServerPackageSafeSettings -PublishDirectory $publish
            if($Mutation -eq 'unsafe-path'){
                $unsafe=Join-Path $publish $RelativePath
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($unsafe))|Out-Null
                [IO.File]::WriteAllText($unsafe,'fixture-secret-do-not-log')
            }
            if($Mutation -eq 'private-material'){
                [IO.File]::WriteAllText((Join-Path $publish 'extra.json'),'-----BEGIN PRIVATE KEY-----fixture-secret-do-not-log')
            }
            if($Mutation -eq 'web-config-override'){
                [IO.File]::WriteAllText((Join-Path $publish 'web.config'),'<configuration><environmentVariables><environmentVariable name="Password" value="fixture-secret-do-not-log" /></environmentVariables></configuration>')
            }
            if($Mutation -in @('unsanitized','enabled','trust-reintroduced')){
                $settings=[IO.File]::ReadAllText($settingsPath)|ConvertFrom-Json
                if($Mutation -eq 'unsanitized'){$settings.Smtp.User='fixture-secret-reintroduced'}
                if($Mutation -eq 'enabled'){$settings.ModelDistribution.Enabled=$true}
                if($Mutation -eq 'trust-reintroduced'){$settings.ModelDistribution.PublicKeysPem|Add-Member -NotePropertyName fixture -NotePropertyValue 'public-not-secret'}
                [IO.File]::WriteAllText($settingsPath,($settings|ConvertTo-Json -Depth 20))
            }
            if($Mutation -eq 'malformed'){[IO.File]::WriteAllText($settingsPath,'{"fixture-secret-do-not-log": invalid}')}
            Assert-ServerPackageSafe -PublishDirectory $publish
            [IO.File]::WriteAllText((Join-Path $Root 'archive.marker'),'only-after-audit')
            [pscustomobject]@{Safe=$true}|ConvertTo-Json -Compress
            """, new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner,
                     "-ModulePath", Path.Combine(FindRepositoryRoot(), "scripts", "ServerPackageSafety.psm1"),
                     "-Root", _root, "-Mutation", mutation }) start.ArgumentList.Add(argument);
        if (relativePath.Length > 0)
        {
            start.ArgumentList.Add("-RelativePath");
            start.ArgumentList.Add(relativePath);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    private static string FindRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory, Path.GetDirectoryName(sourceFile)! })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Server package tests require the source checkout.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
