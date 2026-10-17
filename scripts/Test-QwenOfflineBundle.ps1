param(
    [string]$PublishRoot = '',
    [string]$ModelPath = '',
    [switch]$ArtifactOnly,
    [switch]$AcceptResourceWarning,
    [ValidateRange(0, 100)][int]$Iterations = 0,
    [string]$ResultPath = ''
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DesktopOfflinePackage.psm1') -Force
$approved = Get-DesktopQwenDefinition
if (-not $ModelPath) {
    $base = if ($PublishRoot) { [IO.Path]::GetFullPath($PublishRoot) } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')) }
    $ModelPath = Join-Path $base $approved.RelativePath
}
$null = Test-DesktopQwenModel $ModelPath
if ($ArtifactOnly) {
    [pscustomobject]@{ ModelSha256 = $approved.Sha256; ModelSizeBytes = $approved.SizeBytes; ArtifactHashesVerified = $true; RuntimeProbed = $false }
    return
}
if (-not $PublishRoot -or -not [IO.Path]::GetFullPath($ModelPath).Equals(
    [IO.Path]::GetFullPath((Join-Path $PublishRoot $approved.RelativePath)), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Runtime probe must use the model inside the published application.'
}
if (-not ('StabilityProcessJob' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'StabilityProcessJob.cs') }
$workspace = Join-Path ([IO.Path]::GetTempPath()) ('vm-qwen-offline-' + [Guid]::NewGuid().ToString('N'))
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path ([IO.Path]::GetFullPath($PublishRoot)) 'TOOL-LOCAL.exe'
$start.Arguments = '--check-qwen-offline --workspace "' + $workspace.TrimEnd('\') + '"'
if ($AcceptResourceWarning) { $start.Arguments += ' --accept-resource-warning' }
if ($Iterations -gt 0) { $start.Arguments += ' --iterations ' + $Iterations }
$start.WorkingDirectory = [IO.Path]::GetTempPath()
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.EnvironmentVariables['PATH'] = [Environment]::SystemDirectory + ';' + $env:SystemRoot
$start.EnvironmentVariables.Remove('VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE')
$process = New-Object Diagnostics.Process
$process.StartInfo = $start
$job = New-Object StabilityProcessJob
try {
    if (-not $process.Start()) { throw 'Cannot start the Qwen offline probe.' }
    $job.Assign($process)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $deadlineMs = if ($Iterations -gt 0) { 7260000 } else { 1800000 }
    if (-not $process.WaitForExit($deadlineMs)) { throw 'Qwen offline probe timed out.' }
    $null = $stderr.GetAwaiter().GetResult()
    try { $report = $stdout.GetAwaiter().GetResult() | ConvertFrom-Json }
    catch { throw 'Qwen offline probe did not return a valid diagnostic report.' }
    if ($ResultPath) {
        $resultFile = [IO.Path]::GetFullPath($ResultPath)
        if (Test-Path -LiteralPath $resultFile) { throw 'Diagnostic result file already exists.' }
        $resultWriter = [IO.StreamWriter]::new([IO.File]::Open($resultFile, [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::None), [Text.UTF8Encoding]::new($false))
        try { $resultWriter.Write(($report | ConvertTo-Json -Depth 8)) } finally { $resultWriter.Dispose() }
    }
    if ($process.ExitCode -ne 0 -or $report.QwenOfflineReady -ne $true -or
        $report.NetworkDownloadsAllowed -ne $false -or $report.NetworkAttempts -ne 0 -or
        $report.ModelSha256 -cne $approved.Sha256) {
        $code = if ($report.ErrorCode -match '^[A-Za-z0-9_]{1,80}$') { $report.ErrorCode } else { 'invalid_report' }
        $stage = if ($report.Stage -match '^[A-Za-z0-9_]{1,80}$') { $report.Stage } else { 'unknown' }
        $type = if ($report.WorkerErrorType -match '^[A-Za-z0-9_]{1,80}$') { $report.WorkerErrorType } else { 'unknown' }
        $exitHex = if ($report.WorkerExitCodeHex -match '^0x[0-9A-F]{8}$') { $report.WorkerExitCodeHex } else { 'unknown' }
        $workerStage = if ($report.WorkerStage -match '^[A-Z_]{1,40}$') { $report.WorkerStage } else { 'unknown' }
        $diagnosticId = if ($report.DiagnosticId -match '^[0-9a-f]{32}$') { $report.DiagnosticId } else { 'unavailable' }
        throw "Qwen offline probe failed: code=$code; stage=$stage; worker_stage=$workerStage; exit=$exitHex; diagnostic=$diagnosticId; worker_error_type=$type"
    }
    if ($Iterations -gt 0 -and ($report.CompletedIterations -ne $Iterations -or $report.RequestedIterations -ne $Iterations -or
        ($Iterations -ge 30 -and $report.StabilityVerified -ne $true))) {
        throw 'Qwen did not complete the requested stability qualification.'
    }
    $report
} finally {
    # Dispose the process job before removing files; no orphan worker can retain the model.
    $job.Dispose()
    $process.Dispose()
    if (Test-Path -LiteralPath $workspace) {
        $owned = [IO.Path]::GetFullPath($workspace)
        $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $owned.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase) -or
            (Get-Item -LiteralPath $owned -Force).Attributes -band [IO.FileAttributes]::ReparsePoint -or
            @(Get-ChildItem -LiteralPath $owned -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
            throw 'Refusing cleanup of an unsafe Qwen probe workspace.'
        }
        Remove-Item -LiteralPath $owned -Recurse -Force
    }
}
