using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_TESTS.Updates;

public sealed class DesktopAuthenticodeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-signing-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UnsignedDefaultMakesNoCertificateOrSigningCallsAndRequireFlagRejectsIt()
    {
        var result = await RunHelperAsync("none");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("Signed").GetBoolean());
        Assert.Empty(ReadCalls());
        var required = await RunHelperAsync("require-none");
        Assert.NotEqual(0, required.ExitCode);
        Assert.Empty(ReadCalls());
    }

    [Theory]
    [InlineData("thumbprint")]
    [InlineData("timestamp-http")]
    [InlineData("timestamp-query")]
    [InlineData("missing-cert")]
    [InlineData("no-private")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("wrong-eku")]
    public async Task InvalidSigningConfigurationFailsBeforeAnySigning(string mutation)
    {
        var result = await RunHelperAsync(mutation);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(ReadCalls());
        Assert.DoesNotContain("fixture-secret", result.Output + result.Error);
    }

    [Theory]
    [InlineData("CurrentUser")]
    [InlineData("LocalMachine")]
    public async Task SignsOnlyProjectExecutablesWithExactCertificateSha256AndTimestamp(string store)
    {
        var result = await RunHelperAsync("success", store);
        Assert.True(result.ExitCode == 0, result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("Signed").GetBoolean());
        Assert.True(json.RootElement.GetProperty("Timestamped").GetBoolean());
        Assert.Equal(3, json.RootElement.GetProperty("Files").GetArrayLength());
        var calls = ReadCalls();
        Assert.Equal(3, calls.Count(call => call[0] == "sign"));
        Assert.Equal(3, calls.Count(call => call[0] == "verify"));
        foreach (var call in calls.Where(call => call[0] == "sign"))
        {
            Assert.Equal(new string('A', 40), call[Array.IndexOf(call, "/sha1") + 1]);
            Assert.Equal("SHA256", call[Array.IndexOf(call, "/fd") + 1]);
            Assert.Equal("SHA256", call[Array.IndexOf(call, "/td") + 1]);
            Assert.True(Array.IndexOf(call, "/tr") < Array.IndexOf(call, "/td"));
            Assert.Equal(store == "LocalMachine", call.Contains("/sm"));
            Assert.DoesNotContain("ffmpeg.exe", call[^1]);
            Assert.DoesNotContain("python.exe", call[^1]);
        }
        Assert.Equal("third-party-original", await File.ReadAllTextAsync(Path.Combine(_root, "publish", "tools", "ffmpeg.exe")));
        foreach (var file in json.RootElement.GetProperty("Files").EnumerateArray())
        {
            var path = Path.Combine(_root, "publish", file.GetProperty("Path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))), file.GetProperty("Sha256").GetString());
        }
    }

    [Theory]
    [InlineData("wrong-signer")]
    [InlineData("missing-timestamp")]
    [InlineData("sign-failed")]
    [InlineData("verify-failed")]
    [InlineData("broken-existing")]
    public async Task SigningOrVerificationFailureCannotReturnSignedSuccess(string mutation)
    {
        var result = await RunHelperAsync(mutation);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("\"Signed\":true", result.Output);
    }

    [Fact]
    public async Task MissingRequiredProjectExecutableFailsBeforeAnySigning()
    {
        var result = await RunHelperAsync("missing-worker");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(ReadCalls());
    }

    [Fact]
    public async Task SetupUsesItsOwnExactTargetAndVerifiesBeforeReturningItsHash()
    {
        var result = await RunHelperAsync("setup");
        Assert.True(result.ExitCode == 0, result.Error);
        var calls = ReadCalls();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.EndsWith("VideoMaker Setup.exe", call[^1]));
        using var json = JsonDocument.Parse(result.Output);
        Assert.Single(json.RootElement.GetProperty("Files").EnumerateArray());
    }

    [Fact]
    public void PublishScriptSealsSignedBytesBeforeInventoryZipAndSetupChecksums()
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "Publish-DesktopRelease.ps1"));
        var signDesktop = text.IndexOf("$desktopSigning = Invoke-DesktopAuthenticodeSigning", StringComparison.Ordinal);
        var copyPublish = text.IndexOf("foreach ($file in $publishFiles)", StringComparison.Ordinal);
        var verifyPackage = text.IndexOf("Test-DesktopAuthenticodeSignatures -Root $packageRoot", StringComparison.Ordinal);
        var inventory = text.IndexOf("New-DesktopPackageInventory -PackageRoot $packageRoot", StringComparison.Ordinal);
        var zip = text.IndexOf("New-DesktopPackageZip -PackageRoot $packageRoot", StringComparison.Ordinal);
        var verifyExtracted = text.IndexOf("Test-DesktopAuthenticodeSignatures -Root $extractedRoot", StringComparison.Ordinal);
        var signSetup = text.IndexOf("$setupSigning = Invoke-DesktopAuthenticodeSigning", StringComparison.Ordinal);
        var copySetup = text.IndexOf("Copy-Item -LiteralPath $setupExecutable", StringComparison.Ordinal);
        var verifySetup = text.IndexOf("Test-DesktopAuthenticodeSignatures -Root $setupRoot", StringComparison.Ordinal);
        var setupHash = text.IndexOf("$setupHash = (Get-FileHash", StringComparison.Ordinal);
        Assert.True(signDesktop >= 0 && signDesktop < copyPublish && copyPublish < verifyPackage && verifyPackage < inventory && inventory < zip);
        Assert.True(zip < verifyExtracted && verifyExtracted < signSetup && signSetup < copySetup && copySetup < verifySetup && verifySetup < setupHash);
        Assert.Contains("[string]$AuthenticodeMode = 'None'", text);
        Assert.Contains("-RequireReleaseApproval", text);
        Assert.Contains("ProductionQualified = $false", text);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunHelperAsync(string mutation, string store = "CurrentUser")
    {
        Directory.CreateDirectory(_root);
        var runner = Path.Combine(_root, "run.ps1");
        await File.WriteAllTextAsync(runner, """
            param($ModulePath,$Root,$Mutation,$Store)
            $ErrorActionPreference='Stop'
            [Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
            $module=Import-Module $ModulePath -Force -PassThru
            & $module {
                param($Root,$Mutation)
                $script:fixtureRoot=$Root; $script:mutation=$Mutation
                function script:Get-DesktopSigningCertificate {
                    param($Thumbprint,$StoreLocation)
                    if($script:mutation -eq 'missing-cert'){throw 'certificate missing'}
                    [pscustomobject]@{Thumbprint=$Thumbprint;HasPrivateKey=($script:mutation -ne 'no-private');
                        NotBeforeUtc=$(if($script:mutation -eq 'future'){[DateTime]::UtcNow.AddDays(1)}else{[DateTime]::UtcNow.AddDays(-1)});
                        NotAfterUtc=$(if($script:mutation -eq 'expired'){[DateTime]::UtcNow.AddDays(-1)}else{[DateTime]::UtcNow.AddDays(1)});
                        CodeSigningUsage=($script:mutation -ne 'wrong-eku')}
                }
                function script:Get-DesktopAuthenticodeSignature {
                    param($Path)
                    $signed=[IO.File]::ReadAllText($Path).EndsWith('|fixture-signed')
                    if($script:mutation -eq 'broken-existing'){$status='HashMismatch'}
                    elseif($signed){$status='Valid'}else{$status='NotSigned'}
                    $thumb=if($script:mutation -eq 'wrong-signer'){'B'*40}else{'A'*40}
                    [pscustomobject]@{Status=$status;SignerCertificate=[pscustomobject]@{Thumbprint=$thumb};
                        TimeStamperCertificate=$(if($script:mutation -eq 'missing-timestamp'){$null}else{[pscustomobject]@{Thumbprint='C'*40}})}
                }
                function script:Invoke-DesktopSignTool {
                    param($Policy,[string[]]$Arguments)
                    [IO.File]::AppendAllText((Join-Path $script:fixtureRoot 'calls.jsonl'),(ConvertTo-Json -InputObject @($Arguments) -Compress)+[Environment]::NewLine)
                    if($Arguments[0] -eq 'sign'){
                        if($script:mutation -eq 'sign-failed'){throw 'fixture signing failure'}
                        [IO.File]::AppendAllText($Arguments[-1],'|fixture-signed')
                    }elseif($script:mutation -eq 'verify-failed'){throw 'fixture verification failure'}
                }
            } $Root $Mutation
            $publish=Join-Path $Root 'publish'
            [IO.Directory]::CreateDirectory((Join-Path $publish '_translation_worker'))|Out-Null
            [IO.Directory]::CreateDirectory((Join-Path $publish 'tools'))|Out-Null
            foreach($name in @('TOOL-LOCAL.exe','VideoMaker.Updater.exe','_translation_worker/VideoMaker.Vietsub.TranslationWorker.exe','VideoMaker Setup.exe')){
                if($Mutation -ne 'missing-worker' -or -not $name.StartsWith('_translation_worker/')){[IO.File]::WriteAllText((Join-Path $publish $name),'fixture-project')}
            }
            [IO.File]::WriteAllText((Join-Path $publish 'tools/ffmpeg.exe'),'third-party-original')
            $tool=Join-Path $Root 'signtool.exe';[IO.File]::WriteAllText($tool,'fixture-unused-tool')
            $arguments=@{Mode='WindowsStore';CertificateThumbprint=('A'*40);CertificateStoreLocation=$Store;SignToolPath=$tool;TimestampUrl='https://timestamp.fixture.invalid/'}
            if($Mutation -eq 'none'){$arguments=@{Mode='None'}}
            if($Mutation -eq 'require-none'){$arguments=@{Mode='None';RequireAuthenticode=$true}}
            if($Mutation -eq 'thumbprint'){$arguments.CertificateThumbprint='invalid'}
            if($Mutation -eq 'timestamp-http'){$arguments.TimestampUrl='http://timestamp.fixture.invalid/'}
            if($Mutation -eq 'timestamp-query'){$arguments.TimestampUrl='https://timestamp.fixture.invalid/?password=fixture-secret'}
            $policy=New-DesktopAuthenticodePolicy @arguments
            $kind=if($Mutation -eq 'setup'){'Setup'}else{'Desktop'}
            Invoke-DesktopAuthenticodeSigning -Root $publish -Kind $kind -Policy $policy|ConvertTo-Json -Compress -Depth 6
            """, new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner,
                     "-ModulePath", Path.Combine(FindRepositoryRoot(), "scripts", "DesktopAuthenticode.psm1"),
                     "-Root", _root, "-Mutation", mutation, "-Store", store }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    private List<string[]> ReadCalls()
    {
        var path = Path.Combine(_root, "calls.jsonl");
        return File.Exists(path) ? File.ReadAllLines(path).Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToList() : [];
    }

    private static string FindRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return directory.FullName;
        if (File.Exists(Path.Combine(Environment.CurrentDirectory, "TOOL_GEN_POST_VIDEO.slnx"))) return Environment.CurrentDirectory;
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Authenticode publishing tests require the source checkout.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
