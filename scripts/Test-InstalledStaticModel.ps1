param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true)][ValidateSet('qwen', 'piper')][string]$Engine,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1, 100)][int]$Iterations = 30,
    [ValidateRange(1, 10)][int]$ColdStarts = 1,
    [switch]$AcceptResourceWarning,
    [ValidateSet('', 'qwen3-cpu-safe-v2', 'qwen3-cpu-low-memory-v1')][string]$ConfirmedResourceProfile = ''
)

# Uses the installed application only. No downloads, repair, development model
# discovery, options/SQL/provider access, or production receipt refresh.
$ErrorActionPreference = 'Stop'
if ([bool]$AcceptResourceWarning -ne [bool]$ConfirmedResourceProfile -or
    ($Engine -eq 'piper' -and $AcceptResourceWarning)) { throw 'Resource consent must identify the exact Qwen profile.' }
$applicationRoot = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
$exe = Join-Path $applicationRoot 'TOOL-LOCAL.exe'
foreach ($path in @($applicationRoot, $outputRoot)) {
    if ($path -match '["\r\n]' -or $path.StartsWith('\\') -or $path.Length -gt 170) {
        throw 'Use a short local application and QA path without quotes or network shares.'
    }
    for ($folder = [IO.DirectoryInfo]::new($path); $null -ne $folder; $folder = $folder.Parent) {
        if ($folder.Exists -and ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Qualification paths cannot use reparse points.'
        }
    }
}
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Installed desktop executable is missing.' }
if (Test-Path -LiteralPath $outputRoot) { throw 'QA output must be a new directory.' }
if ($outputRoot.Equals($applicationRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($applicationRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $applicationRoot.StartsWith($outputRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'QA output must be separate from the installed application.'
}
Import-Module (Join-Path $PSScriptRoot 'InstalledQualificationProcessJob.psm1') -Force
$desktopSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$harnessSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
$jobHelperSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'InstalledQualificationProcessJob.psm1') -Algorithm SHA256).Hash.ToLowerInvariant()
$runs = @()
$passed = $false
$failureCode = $null
$hasNativeEvidence = $false
try {
    for ($index = 1; $index -le $ColdStarts; $index++) {
        if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant() -cne $desktopSha256) {
            throw 'Installed executable changed during qualification.'
        }
        $runOutput = Join-Path $outputRoot ('cold-start-{0:D2}' -f $index)
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $exe
        $start.Arguments = '--qualify-installed-static-models --engine ' + $Engine +
            ' --output "' + $runOutput + '" --iterations ' + $Iterations
        if ($AcceptResourceWarning) {
            $start.Arguments += ' --accept-resource-warning --confirmed-resource-profile ' + $ConfirmedResourceProfile
        }
        # The native command creates its output only after trust, receipt and
        # production-root guards pass. Do not create even the parent here.
        $start.WorkingDirectory = $applicationRoot
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.EnvironmentVariables['PATH'] = [Environment]::SystemDirectory + ';' + $env:SystemRoot
        $start.EnvironmentVariables.Remove('VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE')
        $start.EnvironmentVariables.Remove('VIDEOMAKER_QWEN_DIAGNOSTIC_COMPONENT_ROOT')
        $start.EnvironmentVariables['DOTNET_DISABLE_GUI_ERRORS'] = '1'
        $process = New-Object Diagnostics.Process
        $process.StartInfo = $start
        $job = New-InstalledQualificationProcessJob
        try {
            if (-not $process.Start()) { throw 'Cannot start the installed qualification command.' }
            $job.Assign($process)
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(7260000)) { throw 'Installed qualification timed out.' }
            $raw = $stdout.GetAwaiter().GetResult()
            $null = $stderr.GetAwaiter().GetResult()
            if ($raw.Length -gt 262144) { throw 'Installed qualification report exceeded its bound.' }
            try { $report = $raw | ConvertFrom-Json } catch { throw 'Invalid installed qualification report.' }
            if ($report.ErrorCode -match '^[A-Za-z0-9_-]{1,100}$') { $failureCode = $report.ErrorCode }
            $runs += $report
            $diskReport = Join-Path $runOutput 'qualification.json'
            if (Test-Path -LiteralPath $diskReport -PathType Leaf) {
                $nativeFile = Get-Item -LiteralPath $diskReport -Force
                if ($nativeFile.Length -le 0 -or $nativeFile.Length -gt 262144 -or
                    ($nativeFile.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                    throw 'Invalid native QA evidence file.'
                }
                $nativeRaw = [IO.File]::ReadAllText($diskReport)
                if ($nativeRaw.Trim() -cne $raw.Trim() -or $report.SchemaVersion -ne 1 -or
                    $report.Kind -cne 'installed-static-model-qualification' -or $report.Engine -cne $Engine -or
                    $report.BundleId -cne 'models-20261016-03' -or
                    $report.ArchiveSha256 -cne 'e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471' -or
                    $report.Binding.OperationId -notmatch '^[0-9a-f]{32}$' -or
                    $report.Binding.RuntimeFingerprint -notmatch '^[0-9a-fA-F]{64}$' -or
                    $report.Binding.MachineFingerprint -notmatch '^[0-9a-fA-F]{64}$' -or
                    $report.Binding.ApplicationFingerprint -notmatch '^[0-9a-fA-F]{64}$') {
                    throw 'Native QA evidence does not match the command report.'
                }
                $hasNativeEvidence = $true
            }
            if ($process.ExitCode -ne 0 -or $report.SchemaVersion -ne 1 -or
                $report.Kind -cne 'installed-static-model-qualification' -or $report.Engine -cne $Engine -or
                $report.BundleId -cne 'models-20261016-03' -or
                $report.ArchiveSha256 -cne 'e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471' -or
                $report.Succeeded -ne $true -or $report.ProductionReadinessChanged -ne $false -or
                $report.BusinessAccessVerified -ne $false -or $report.NetworkDownloadsAllowed -ne $false -or
                $report.RequestedIterations -ne $Iterations -or $report.CompletedIterations -ne $Iterations -or
                @($report.Samples).Count -ne $Iterations -or
                $report.Binding.OperationId -notmatch '^[0-9a-f]{32}$' -or
                $report.Binding.RuntimeFingerprint -notmatch '^[0-9a-fA-F]{64}$' -or
                $report.Binding.MachineFingerprint -notmatch '^[0-9a-fA-F]{64}$' -or
                $report.Binding.ApplicationFingerprint -notmatch '^[0-9a-fA-F]{64}$') {
                $safeCode = if ($failureCode) { $failureCode } else { 'invalid_report' }
                throw "Installed runtime qualification failed: $safeCode"
            }
            if ($Engine -eq 'qwen' -and $Iterations -ge 30 -and
                ($null -eq $report.PrivateMemoryGrowthBytes -or $report.PrivateMemoryGrowthBytes -ge 268435456)) {
                throw 'Installed Qwen exceeded the warmed private-memory growth gate.'
            }
            if (-not $hasNativeEvidence -or -not (Test-Path -LiteralPath $diskReport -PathType Leaf)) {
                throw 'Native QA evidence is missing.'
            }
        } finally {
            $job.Dispose()
            $process.Dispose()
        }
    }
    $passed = $runs.Count -eq $ColdStarts
} finally {
    # Preflight rejection has no native output and must leave no QA directory,
    # including a user-supplied path inside a custom production cache.
    if ($hasNativeEvidence) {
      $series = [ordered]@{ SchemaVersion = 1; Kind = 'installed-static-model-series'; CheckedAtUtc = [DateTime]::UtcNow.ToString('o')
        Engine = $Engine; Passed = $passed; RequestedColdStarts = $ColdStarts; RequestedIterationsPerStart = $Iterations
        DesktopSha256 = $desktopSha256; HarnessSha256 = $harnessSha256; JobHelperSha256 = $jobHelperSha256; ErrorCode = $failureCode
        ResourceWarningAccepted = [bool]$AcceptResourceWarning; ConfirmedResourceProfile = $ConfirmedResourceProfile
        Runs = $runs; ProductionReadinessChanged = $false; BusinessAccessVerified = $false
        CustomerReleaseQualified = $false; OtherMachinesVerified = $false
      } | ConvertTo-Json -Depth 12
      $writer = [IO.StreamWriter]::new([IO.File]::Open((Join-Path $outputRoot 'series.json'),
          [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None), [Text.UTF8Encoding]::new($false))
      try { $writer.Write($series) } finally { $writer.Dispose() }
    }
}
if (-not $passed) { throw 'Installed model qualification did not complete.' }
Get-Content -LiteralPath (Join-Path $outputRoot 'series.json') -Raw | ConvertFrom-Json
