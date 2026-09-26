#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('RegressionPair', 'RegressionBridge', 'RegressionCloud', 'Full', 'Frontend', 'Native', 'Timeline', 'Piper', 'SetupOffline')]
    [string]$Profile = 'RegressionPair',
    [ValidateRange(1, 1000)][int]$Iterations = 1,
    [string]$OutputRoot = (Join-Path ([IO.Path]::GetPathRoot($PSScriptRoot)) 'vmtest'),
    [ValidateRange(30, 3600)][int]$TimeoutSeconds = 600,
    [switch]$KeepTemp,
    [switch]$InstallPiper
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$web = Join-Path $repo 'TOOL-LOCAL/Web'
$assembly = Join-Path $repo 'TOOL-TESTS/bin/Release/net10.0-windows/TOOL-TESTS.dll'
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$node = (Get-Command node -ErrorAction Stop).Source
if ($Profile -eq 'Piper' -and !$InstallPiper) { throw 'Piper requires -InstallPiper and the approved offline payload copied by the Release build.' }
if ($Profile -eq 'Piper' -and !$PSBoundParameters.ContainsKey('TimeoutSeconds')) { $TimeoutSeconds = 1800 }
if (!(Test-Path -LiteralPath $assembly)) { throw 'Build Release and the frontend before running stability checks.' }
if (!(Test-Path -LiteralPath (Join-Path $web 'node_modules/vitest/vitest.mjs'))) { throw 'Run npm ci before stability checks.' }
if (-not ('StabilityProcessJob' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'StabilityProcessJob.cs') }
. (Join-Path $PSScriptRoot 'StabilityReport.ps1')

$series = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Profile + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$null = New-Item -ItemType Directory -Path $series -Force
$series = (Resolve-Path -LiteralPath $series).Path
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($series))
if ($drive.AvailableFreeSpace -lt 3GB) { throw 'At least 3 GB free is required for isolated test workspaces.' }
$utf8 = [Text.UTF8Encoding]::new($false)
function Write-Json($Value, [string]$Path) { [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 18), $utf8) }
function Get-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Quote-Argument([string]$Value) {
    if ($Value.IndexOfAny([char[]]@([char]0, [char]10, [char]13)) -ge 0) { throw 'Invalid process argument.' }
    $quoted = [regex]::Replace($Value, '(\\*)"', { param($m) $m.Groups[1].Value + $m.Groups[1].Value + '\"' })
    $quoted = [regex]::Replace($quoted, '(\\+)$', '$1$1')
    '"' + $quoted + '"'
}
function Remove-OwnedTemp([string]$Path) {
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (!$resolved.StartsWith($series + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing cleanup outside this test series.'
    }
    # Python wheel license paths can exceed MAX_PATH. PowerShell 5.1 needs the
    # extended-length form even though .NET 10 and uv installed them correctly.
    $nativePath = if ($resolved.StartsWith('\\')) { '\\?\UNC\' + $resolved.Substring(2) } else { '\\?\' + $resolved }
    if ((Get-Item -LiteralPath $nativePath).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Refusing recursive cleanup of a reparse point.'
    }
    $links = @(Get-ChildItem -LiteralPath $nativePath -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
    if ($links.Count -gt 0) { throw 'Refusing recursive cleanup of a workspace containing reparse points.' }
    Remove-Item -LiteralPath $nativePath -Recurse -Force
}
function Invoke-OwnedProcess([string]$Executable, [string[]]$Arguments, [string]$Directory, [string]$Prefix, [string]$Temp) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.Arguments = ($Arguments | ForEach-Object { Quote-Argument $_ }) -join ' '
    $start.WorkingDirectory = $Directory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['TEMP'] = $Temp
    $start.EnvironmentVariables['TMP'] = $Temp
    $start.EnvironmentVariables['NO_COLOR'] = '1'
    $start.EnvironmentVariables['MSBUILDDISABLENODEREUSE'] = '1'
    # Opt-in tests never inherit an unrelated shell's model/database configuration.
    foreach ($name in @('VIDEOMAKER_RUN_SETUP_PIPER', 'VIDEOMAKER_RUN_TIKTOK_SQL_TESTS',
        'VIDEOMAKER_RUN_GPU_MODEL_TESTS', 'VIDEOMAKER_RUN_GPU_NATIVE_TESTS', 'VIDEOMAKER_RUN_LOCAL_MODEL_TESTS',
        'VIDEOMAKER_RUN_TRANSLATION_BENCHMARKS', 'VIDEOMAKER_RUN_TRANSLATION_PERFORMANCE_TESTS', 'VIDEOMAKER_RUN_LOCAL_VOICE_TESTS')) {
        $start.EnvironmentVariables[$name] = '0'
    }
    $start.EnvironmentVariables.Remove('VM_LOCAL_VOICE_SMOKE_SOURCE')
    $start.EnvironmentVariables.Remove('VIDEOMAKER_LOGIN_SCREENSHOT')
    if ($Profile -eq 'Piper') {
        $start.EnvironmentVariables['VIDEOMAKER_RUN_SETUP_PIPER'] = '1'
        $start.EnvironmentVariables['VIDEOMAKER_SETUP_PIPER_WORKSPACE'] = Join-Path $Temp 'piper-clean'
        $start.EnvironmentVariables['UV_CACHE_DIR'] = Join-Path $Temp 'uv-cache'
        $start.EnvironmentVariables['UV_PYTHON_INSTALL_DIR'] = Join-Path $Temp 'uv-python'
        $start.EnvironmentVariables['HTTP_PROXY'] = 'http://127.0.0.1:9'
        $start.EnvironmentVariables['HTTPS_PROXY'] = 'http://127.0.0.1:9'
        $start.EnvironmentVariables['ALL_PROXY'] = 'http://127.0.0.1:9'
        $start.EnvironmentVariables['NO_PROXY'] = ''
    }
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    $job = New-Object StabilityProcessJob
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    $peakChildren = 0
    $peakParentMemory = 0L
    $remaining = 0
    try {
        if (!$process.Start()) { throw 'Could not start the test process.' }
        try { $job.Assign($process) } catch { $process.Kill(); throw }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        while (!$process.WaitForExit(1000)) {
            $peakChildren = [Math]::Max($peakChildren, $job.ActiveProcesses)
            $process.Refresh()
            $peakParentMemory = [Math]::Max($peakParentMemory, $process.PeakWorkingSet64)
            if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { $timedOut = $true; break }
        }
        $exitCode = if ($timedOut) { -1 } else { $process.ExitCode }
        $grace = [Diagnostics.Stopwatch]::StartNew()
        while (!$timedOut -and $job.ActiveProcesses -gt 0 -and $grace.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 100 }
        $remaining = $job.ActiveProcesses
        $peakCommittedBytes = $job.PeakCommittedBytes
    }
    finally {
        $job.Dispose() # Terminates only this invocation's tree, including an orphaned test host.
        if ($stdout) { [IO.File]::WriteAllText($Prefix + '.stdout.log', $stdout.GetAwaiter().GetResult(), $utf8) }
        if ($stderr) { [IO.File]::WriteAllText($Prefix + '.stderr.log', $stderr.GetAwaiter().GetResult(), $utf8) }
        $process.Dispose()
    }
    [pscustomobject]@{ ExitCode = $exitCode; TimedOut = $timedOut; Seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2);
        RemainingProcessesBeforeCleanup = $remaining; PeakActiveProcesses = $peakChildren;
        PeakParentWorkingSetBytes = $peakParentMemory; PeakTreeCommittedBytes = $peakCommittedBytes }
}

$bridgeTest = 'TOOL_TESTS.Vietsub.VietsubTranslationBridgeTests.Bridge_runtime_install_passes_only_explicit_resource_confirmation'
$cloudTest = 'TOOL_TESTS.Vietsub.VietsubCloudDesktopTests.Cloud_TranslatesEntireTrackAndPreservesManualLockedAndValidLocalCues'
$filter = switch ($Profile) {
    'RegressionPair' { "FullyQualifiedName=$bridgeTest|FullyQualifiedName=$cloudTest" }
    'RegressionBridge' { "FullyQualifiedName=$bridgeTest" }
    'RegressionCloud' { "FullyQualifiedName=$cloudTest" }
    'Timeline' { 'FullyQualifiedName~VietsubTimelineLayoutIntegrationTests' }
    'Native' { '(FullyQualifiedName~SystemSetupAdapterTests|FullyQualifiedName~VietsubPaddleOcrIntegrationTests|FullyQualifiedName~LoginWebViewIntegrationTests|FullyQualifiedName~VietsubSubtitleEditorBrowserTests|FullyQualifiedName~VietsubTimelineLayoutIntegrationTests|FullyQualifiedName~VietsubVoicePlaybackIntegrationTests|FullyQualifiedName~VietsubWebView2MediaIntegrationTests|FullyQualifiedName~ShortVideoComposerBrowserTests)&Category!=SetupPiperIntegration' }
    'Piper' { 'Category=SetupPiperIntegration' }
    'SetupOffline' { '(FullyQualifiedName~PiperOfflineBundleTests|FullyQualifiedName~StartupSystemSetupWorkflowTests|FullyQualifiedName~SystemSetupTests|FullyQualifiedName~RuntimeUseGateProcessTests)&FullyQualifiedName!~WebView2_UsesRealSetupBridgeWithoutAProject' }
    default { '' }
}
$testArgs = @('test', (Join-Path $repo 'TOOL-TESTS/TOOL-TESTS.csproj'), '-c', 'Release', '--no-build', '--no-restore')
if ($filter) { $testArgs += @('--filter', $filter) }

$sourceFiles = @(& git -C $repo ls-files --cached --others --exclude-standard | Sort-Object -Unique | Where-Object {
    $_ -match '\.(cs|csproj|props|targets|json|ts|tsx|ps1|lock|py|sql|slnx|yaml|yml|toml)$' -and (Test-Path -LiteralPath (Join-Path $repo $_))
})
$sourceManifest = @($sourceFiles | ForEach-Object { [pscustomobject]@{ Path = $_; Sha256 = Get-Hash (Join-Path $repo $_) } })
$binaryFiles = @('TOOL-TESTS.dll', 'TOOL-LOCAL.dll', 'TOOL-SERVER.dll', 'TOOL-SHARED.Contracts.dll',
    'VideoMaker.Updater.dll', 'TOOL-SHARED.Distribution.dll', 'xunit.runner.json',
    '_translation_worker/VideoMaker.Vietsub.TranslationWorker.dll') |
    ForEach-Object { Join-Path (Split-Path $assembly) $_ }
$binaryFiles += @(Get-ChildItem -LiteralPath (Join-Path $web 'dist') -Recurse -File | ForEach-Object FullName)
$binaryFiles += @(Get-ChildItem -LiteralPath (Split-Path $assembly) -File | Where-Object {
    $_.Name -in @('paddle_inference_c.dll','OpenCvSharpExtern.dll','Sdcb.PaddleOCR.Models.LocalV5.dll')
} | ForEach-Object FullName)
$piperBinaryRoot = Join-Path (Split-Path $assembly) 'components/piper'
if (Test-Path -LiteralPath $piperBinaryRoot) {
    $binaryFiles += @(Get-ChildItem -LiteralPath $piperBinaryRoot -Recurse -File | ForEach-Object FullName)
}
$binaryFiles += @(Get-ChildItem -LiteralPath (Join-Path (Split-Path $assembly) 'workers') -File |
    Where-Object { $_.Name -in @('piper_worker.py','piper-requirements.lock') } | ForEach-Object FullName)
$binaryManifest = @($binaryFiles | ForEach-Object { [pscustomobject]@{ Path = $_; Sha256 = Get-Hash $_ } })
Write-Json $sourceManifest (Join-Path $series 'source-manifest.json')
Write-Json $binaryManifest (Join-Path $series 'binary-manifest.json')
$metadata = [ordered]@{
    StartedUtc = [DateTime]::UtcNow.ToString('o'); Profile = $Profile; RequestedIterations = $Iterations;
    Branch = (& git -C $repo branch --show-current); Commit = (& git -C $repo rev-parse HEAD);
    SourceManifestSha256 = Get-Hash (Join-Path $series 'source-manifest.json');
    BinaryManifestSha256 = Get-Hash (Join-Path $series 'binary-manifest.json');
    DotnetSdk = (& $dotnet --version); Node = (& $node --version); Npm = (& npm --version);
    Windows = [Environment]::OSVersion.VersionString; ProcessorCount = [Environment]::ProcessorCount;
    PhysicalMemoryBytes = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory;
    FreeDiskBytes = $drive.AvailableFreeSpace; Filter = $filter; TimeoutSeconds = $TimeoutSeconds;
    XUnit = (Get-Content -LiteralPath (Join-Path (Split-Path $assembly) 'xunit.runner.json') -Raw | ConvertFrom-Json);
    FrontendWorkers = 4; SourceDirty = [bool](& git -C $repo status --porcelain);
    WebView2Versions = @(Get-ChildItem -Path "${env:ProgramFiles(x86)}/Microsoft/EdgeWebView/Application" -Directory -ErrorAction SilentlyContinue |
        Where-Object Name -match '^\d+\.' | ForEach-Object Name)
}
Write-Json $metadata (Join-Path $series 'metadata.json')
Write-Host "Stability artifacts: $series"

$runs = @()
$expectedIdentity = $null
$requiredNames = @()
if ($Profile -ne 'Frontend') {
    $discovery = Join-Path $series 'discovery'
    $null = New-Item -ItemType Directory -Path $discovery
    $discovered = Invoke-OwnedProcess $dotnet ($testArgs + @('--list-tests')) $repo (Join-Path $series 'discovery') $discovery
    if ($discovered.ExitCode -ne 0 -or $discovered.TimedOut -or $discovered.RemainingProcessesBeforeCleanup -gt 0) { throw 'Test discovery failed; inspect discovery logs.' }
    $requiredNames = @(Get-Content -LiteralPath (Join-Path $series 'discovery.stdout.log') -Encoding UTF8 |
        Where-Object { $_ -match '^\s+TOOL_TESTS\.' } | ForEach-Object { $_.Trim() } | Sort-Object)
    if ($requiredNames.Count -eq 0) { throw 'Discovery returned no tests.' }
    Write-Json $requiredNames (Join-Path $series 'discovered-tests.json')
    Remove-OwnedTemp $discovery
}

for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
    $runRoot = Join-Path $series ('run-{0:D3}' -f $iteration)
    $temp = Join-Path $runRoot 'T có dấu'
    $null = New-Item -ItemType Directory -Path $temp -Force
    $result = [ordered]@{ Iteration = $iteration; Passed = 0; Failed = 0; Skipped = 0; ValidationError = $null; Process = $null }
    try {
        foreach ($file in $binaryManifest) { if ((Get-Hash $file.Path) -ne $file.Sha256) { throw 'Binary changed during the series; rebuild and start a new series.' } }
        if ($Profile -eq 'Frontend') {
            $seed = 260926 + $iteration
            $report = Join-Path $runRoot 'frontend.json'
            $arguments = @((Join-Path $web 'node_modules/vitest/vitest.mjs'), 'run', '--reporter=json', "--outputFile=$report", '--sequence.shuffle', "--sequence.seed=$seed")
            $result.Seed = $seed
            $result.Process = Invoke-OwnedProcess $node $arguments $web (Join-Path $runRoot 'test') $temp
            $json = Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
            $cases = @($json.testResults | ForEach-Object { $file = $_.name; $_.assertionResults | ForEach-Object {
                [pscustomobject]@{ Id = $file + ':' + $_.fullName; Name = $file + ':' + $_.fullName; Outcome = $_.status; Reason = '' }
            } })
            $result.Passed = @($cases | Where-Object Outcome -eq 'passed').Count
            $result.Failed = @($cases | Where-Object Outcome -eq 'failed').Count
            $result.Skipped = @($cases | Where-Object Outcome -in @('pending','skipped','todo','disabled')).Count
            if ($cases.Count -ne $json.numTotalTests -or !$json.success -or $result.Skipped -gt 0) { throw 'Frontend report has failures, skips or inconsistent totals.' }
        } else {
            $result.Process = Invoke-OwnedProcess $dotnet ($testArgs + @('--logger', 'trx;LogFileName=tests.trx', '--results-directory', $runRoot)) $repo (Join-Path $runRoot 'test') $temp
            [xml]$trx = Get-Content -LiteralPath (Join-Path $runRoot 'tests.trx') -Raw -Encoding UTF8
            $cases = @($trx.TestRun.Results.UnitTestResult | ForEach-Object {
                [pscustomobject]@{ Id = $_.testId; Name = $_.testName; Outcome = $_.outcome; Reason = [string]$_.Output.ErrorInfo.Message }
            })
            $result.Passed = @($cases | Where-Object Outcome -eq 'Passed').Count
            $result.Failed = @($cases | Where-Object Outcome -eq 'Failed').Count
            $result.Skipped = @($cases | Where-Object Outcome -eq 'NotExecuted').Count
            $actualNames = @($cases.Name | Sort-Object)
            if (@(Compare-Object $requiredNames $actualNames -CaseSensitive).Count -gt 0) { throw 'Executed cases differ from discovery; inspect the TRX and discovered-tests.json.' }
            $unexpectedSkips = @($cases | Where-Object { $_.Outcome -eq 'NotExecuted' -and
                ($Profile -notin @('Full','Native') -or $_.Reason -notmatch '^(Opt.in|Run explicitly|Requires explicit)') })
            if ($unexpectedSkips.Count -gt 0) { throw 'Unexpected skipped tests; this is not a successful validation.' }
        }
        Write-Json @($cases | Where-Object { $_.Outcome -in @('NotExecuted','pending','skipped','todo','disabled') }) (Join-Path $runRoot 'skips.json')
        if ($cases.Count -eq 0 -or $cases.Count -ne ($result.Passed + $result.Failed + $result.Skipped)) { throw 'Missing or unknown test outcomes.' }
        if ($result.Failed -gt 0 -or $result.Process.ExitCode -ne 0 -or $result.Process.TimedOut -or $result.Process.RemainingProcessesBeforeCleanup -gt 0) {
            throw 'Test failure, timeout or leftover subprocess; inspect this run before rerunning.'
        }
        $identity = Get-StabilityCaseFingerprint $cases
        if ($expectedIdentity -and $identity -ne $expectedIdentity) { throw 'Test identity/outcomes changed between iterations.' }
        $expectedIdentity = $identity
        $result.TestIdentitySha256 = $identity
        if (!$KeepTemp) { Remove-OwnedTemp $temp }
    } catch { $result.ValidationError = $_.Exception.Message }
    Write-Json $result (Join-Path $runRoot 'result.json')
    $runs += [pscustomobject]$result
    Write-Json ([ordered]@{ Metadata = $metadata; Runs = $runs; Complete = $false }) (Join-Path $series 'summary.json')
    Write-Host ("{0} {1}/{2}: Passed={3}, Failed={4}, Skipped={5}, seconds={6}" -f $Profile, $iteration, $Iterations, $result.Passed, $result.Failed, $result.Skipped, $result.Process.Seconds)
    if ($result.ValidationError) { throw $result.ValidationError }
}
# Source changes invalidate the series even when previously built binaries still pass.
$currentSourceFiles = @(& git -C $repo ls-files --cached --others --exclude-standard | Sort-Object -Unique | Where-Object {
    $_ -match '\.(cs|csproj|props|targets|json|ts|tsx|ps1|lock|py|sql|slnx|yaml|yml|toml)$' -and (Test-Path -LiteralPath (Join-Path $repo $_))
})
if (@(Compare-Object $sourceFiles $currentSourceFiles).Count -gt 0) { throw 'Source file list changed during the series.' }
foreach ($file in $sourceManifest) {
    if ((Get-Hash (Join-Path $repo $file.Path)) -ne $file.Sha256) {
        Write-Json ([ordered]@{ Metadata = $metadata; Runs = $runs; Complete = $false; ValidationError = 'Source changed during the series.' }) (Join-Path $series 'summary.json')
        throw 'Source changed during the series. Keep these artifacts and start a new series.'
    }
}
Write-Json ([ordered]@{ Metadata = $metadata; Runs = $runs; Complete = $true }) (Join-Path $series 'summary.json')
Write-Host "Completed $Iterations iteration(s). Reports: $series"
