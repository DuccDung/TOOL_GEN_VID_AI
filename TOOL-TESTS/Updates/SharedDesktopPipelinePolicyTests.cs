using System.Diagnostics;
using System.Text;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Updates;

namespace TOOL_TESTS.Updates;

// Synthetic probe JSON only. Extracts the pure validator function through PowerShell's parser;
// never executes a publish script, native customer executable or remote request.
public sealed class SharedDesktopPipelinePolicyTests
{
    [Fact]
    public void Policy_report_exposes_the_actual_compiled_distribution_capability()
    {
        var report = StaticModelBootstrapCommand.InspectPolicy(() => { });
        Assert.Equal(DesktopBuildInfo.DistributionProtocol, report.DistributionProtocol);
        Assert.Equal(DesktopBuildInfo.Profile, report.Profile);
    }

    [Theory]
    [InlineData("legacy", true)]
    [InlineData("shared", true)]
    [InlineData("wrong-protocol", false)]
    [InlineData("wrong-profile", false)]
    [InlineData("missing-profile", false)]
    [InlineData("missing-expected-profile", false)]
    [InlineData("wrong-version", false)]
    [InlineData("wrong-build", false)]
    [InlineData("wrong-channel", false)]
    [InlineData("wrong-model", false)]
    [InlineData("native-rejected", false)]
    [InlineData("shared-inputs-application", true)]
    [InlineData("shared-inputs-bootstrapper", true)]
    public async Task Actual_probe_identity_must_match_the_requested_shared_or_legacy_capability(string scenario, bool expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "vm-shared-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runner = Path.Combine(root, "validate.ps1");
            await File.WriteAllTextAsync(runner, Runner, new UTF8Encoding(true));
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner,
                "-Validator", Path.Combine(FindRepository(), "scripts", scenario == "shared-inputs-application" ? "Publish-DesktopRelease.ps1"
                    : scenario == "shared-inputs-bootstrapper" ? "Publish-SharedDesktopSetup.ps1" : "Test-DesktopSetupPublish.ps1"), "-Scenario", scenario })
                start.ArgumentList.Add(value);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            await Task.WhenAll(output, error);
            Assert.Equal(expected, process.ExitCode == 0);
        }
        finally
        {
            var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(temporary, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test root.");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string FindRepository()
    {
        for (var item = new DirectoryInfo(AppContext.BaseDirectory); item is not null; item = item.Parent)
            if (File.Exists(Path.Combine(item.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return item.FullName;
        throw new InvalidOperationException("Source checkout required.");
    }

    private const string Runner = """
        param($Validator,$Scenario)
        $ErrorActionPreference='Stop'
        $tokens=$null; $errors=$null
        $ast=[Management.Automation.Language.Parser]::ParseFile($Validator,[ref]$tokens,[ref]$errors)
        if ($errors.Count) { throw 'Policy validator parse failed.' }
        $functionName=if($Scenario.StartsWith('shared-inputs-')){'Assert-SharedDesktopPublishInput'}else{'Assert-DesktopCompiledStaticPolicy'}
        $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName},$true)
        if (-not $definition) { throw 'Missing compiled policy validation function.' }
        . ([scriptblock]::Create($definition.Extent.Text))
        if($Scenario.StartsWith('shared-inputs-')) {
          $cases=@(
            @{Channel='Stable';Version='1.2.0';Expected=$true},
            @{Channel='Stable';Version='2147483647.0.0';Expected=$true},
            @{Channel='Beta';Version='1.2.0';Expected=$false},
            @{Channel='Development';Version='1.2.0';Expected=$false},
            @{Channel='Stable';Version='01.2.0';Expected=$false},
            @{Channel='Stable';Version='2147483648.0.0';Expected=$false},
            @{Channel='Stable';Version='1.2.0-beta';Expected=$false})
          foreach($case in $cases) {
            $accepted=$true
            try { Assert-SharedDesktopPublishInput -Channel $case.Channel -Versions @($case.Version) } catch {$accepted=$false}
            if($accepted -ne $case.Expected) {throw 'Shared channel/version preflight did not enforce the server contract.'}
          }
          exit 0
        }
        $report=[pscustomobject]@{required=$true;BundleId='models-20261016-03';ArchiveSha256='e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471';version='1.2.0';buildNumber=37;channel='Stable';distributionProtocol=2;profile='Production';downloadPolicyVersion=1}
        $arguments=@{PolicyReport=$report;ExitCode=0;ExpectedVersion='1.2.0';ExpectedBuildNumber=37;ExpectedChannel='Stable'}
        if ($Scenario -ne 'legacy') {$arguments.ExpectedDistributionProtocol=3;$arguments.ExpectedProfile='Production';$report.distributionProtocol=3}
        switch($Scenario) {
          'wrong-protocol' {$report.distributionProtocol=2}
          'wrong-profile' {$report.profile='Other'}
          'missing-profile' {$report.PSObject.Properties.Remove('profile')}
          'missing-expected-profile' {$arguments.ExpectedProfile=''}
          'wrong-version' {$report.version='1.1.0'}
          'wrong-build' {$report.buildNumber=36}
          'wrong-channel' {$report.channel='Beta'}
          'wrong-model' {$report.BundleId='other-model'}
          'native-rejected' {$arguments.ExitCode=2}
        }
        Assert-DesktopCompiledStaticPolicy @arguments
        """;
}
