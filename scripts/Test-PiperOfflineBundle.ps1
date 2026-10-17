param(
    [string]$BundleDirectory = '',
    [switch]$ArtifactOnly,
    [string]$PublishRoot = '',
    [string]$Workspace = '',
    [switch]$VerifyOnly,
    [string]$FailureReportPath = ''
)
$ErrorActionPreference = 'Stop'
function Resolve-PiperFailureReportPath([string]$Path) {
    if (-not $Path) { return '' }
    # Windows PowerShell 5.1 lacks Path.IsPathFullyQualified. Reject C:relative and
    # root-relative paths explicitly rather than resolving them against a hidden CWD.
    if ($Path -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+[\\/])') {
        throw 'FailureReportPath must be a new fully qualified diagnostic file.'
    }
    $full = [IO.Path]::GetFullPath($Path)
    if (Test-Path -LiteralPath $full) { throw 'FailureReportPath must not overwrite an existing file.' }
    $parent = [IO.Path]::GetDirectoryName($full)
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw 'FailureReportPath requires an existing diagnostic directory.' }
    for ($item = New-Object IO.DirectoryInfo $parent; $null -ne $item; $item = $item.Parent) {
        if ($item.Exists -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'FailureReportPath cannot traverse a reparse point.'
        }
    }
    return $full
}
function Read-PiperProbeReport([string]$JsonText) {
    # Native crashes can terminate before the managed CLI emits a report. Never
    # echo a parser exception containing arbitrary stdout into diagnostics.
    try {
        if ([string]::IsNullOrWhiteSpace($JsonText) -or $JsonText.Length -gt 65536) { throw 'Invalid report size.' }
        $parsed = $JsonText | ConvertFrom-Json -ErrorAction Stop
        if ($null -eq $parsed -or $parsed.GetType() -ne [System.Management.Automation.PSCustomObject]) { throw 'Invalid report shape.' }
        if ($parsed.PiperOfflineReady -isnot [bool] -or
            ($parsed.PiperOfflineReady -and ($parsed.NetworkDownloadsAllowed -isnot [bool] -or $parsed.NetworkDownloadsAllowed))) {
            throw 'Invalid report status types.'
        }
        return $parsed
    } catch { return [pscustomobject]@{ PiperOfflineReady = $false; ErrorCode = 'piper_offline_invalid_report'; Diagnostic = $null } }
}
function ConvertTo-PiperSafeFailureReport {
    param([object]$Report, [int]$ProcessExitCode)
    $safeCode = if ($Report.ErrorCode -is [string] -and $Report.ErrorCode -cmatch '^[A-Za-z0-9_]{1,80}$') { $Report.ErrorCode } else { 'invalid_report' }
    $safe = [ordered]@{ PiperOfflineReady = $false; ErrorCode = $safeCode; ProcessExitCode = $ProcessExitCode; Diagnostic = $null }
    $details = $Report.Diagnostic
    if ($null -eq $details) { return [pscustomobject]$safe }
    $fields = @('SchemaVersion','Phase','FailureKind','ExitCode','NativeErrorCode','HResult',
        'RuntimeRootCharacters','EmbeddedPythonPathCharacters','EnvironmentPathCharacters')
    if (@($details.PSObject.Properties).Count -ne $fields.Count -or
        @($details.PSObject.Properties | Where-Object { $_.Name -cnotin $fields }).Count -gt 0 -or
        ($details.SchemaVersion -isnot [int] -and $details.SchemaVersion -isnot [long]) -or $details.SchemaVersion -ne 1 -or
        $details.Phase -isnot [string] -or $details.FailureKind -isnot [string] -or
        $details.Phase -cnotin @('UNKNOWN','VALIDATE','EXTRACT','PROMOTE','PYTHON','PACKAGES','PROBE','READY') -or
        $details.FailureKind -cnotin @('PROCESS_EXIT','PATH_TOO_LONG','PROCESS_START_FAILED','ACCESS_DENIED','FILE_IO_FAILED','COMPONENT_REJECTED')) {
        return [pscustomobject]$safe
    }
    foreach ($field in @('ExitCode','NativeErrorCode','HResult')) {
        $value = $details.$field
        if ($null -ne $value -and (($value -isnot [int] -and $value -isnot [long]) -or
            $value -lt [int]::MinValue -or $value -gt [int]::MaxValue)) { return [pscustomobject]$safe }
    }
    foreach ($field in @('RuntimeRootCharacters','EmbeddedPythonPathCharacters','EnvironmentPathCharacters')) {
        $value = $details.$field
        if (($value -isnot [int] -and $value -isnot [long]) -or $value -lt 0 -or $value -gt 100000) {
            return [pscustomobject]$safe
        }
    }
    $approvedDetails = [ordered]@{}
    foreach ($field in $fields) { $approvedDetails[$field] = $details.$field }
    $safe.Diagnostic = [pscustomobject]$approvedDetails
    return [pscustomobject]$safe
}
$FailureReportPath = Resolve-PiperFailureReportPath $FailureReportPath
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$approved = Get-Content -LiteralPath (Join-Path $repoRoot 'third_party/voice/PIPER_OFFLINE_APPROVED.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $BundleDirectory) {
    $base = if ($PublishRoot) { Join-Path $PublishRoot 'components/piper' } else { Join-Path $repoRoot 'artifacts/piper-offline' }
    $BundleDirectory = Join-Path $base $approved.bundleVersion
}
foreach ($entry in @(
    @{ Name = 'piper-offline.zip'; Size = $approved.archiveSize; Hash = $approved.archiveSha256 },
    @{ Name = 'manifest.json'; Size = $approved.manifestSize; Hash = $approved.manifestSha256 }
)) {
    $path = Join-Path $BundleDirectory $entry.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-Item -LiteralPath $path).Length -ne $entry.Size -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Hash) {
        throw ('Piper offline bundle missing or checksum mismatch: ' + $entry.Name)
    }
}
$worker = if ($PublishRoot) { Join-Path $PublishRoot 'workers/piper_worker.py' } else { Join-Path $repoRoot 'TOOL-LOCAL/Vietsub/Voice/Workers/piper_worker.py' }
$lock = if ($PublishRoot) { Join-Path $PublishRoot 'workers/piper-requirements.lock' } else { Join-Path $repoRoot 'TOOL-LOCAL/SystemSetup/piper-requirements.lock' }
if ((Get-FileHash -LiteralPath $worker -Algorithm SHA256).Hash -ne $approved.workerSha256 -or
    (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash -ne $approved.requirementsSha256) {
    throw 'Piper worker or requirements do not match the approved offline bundle.'
}
if ($ArtifactOnly) {
    [pscustomobject]@{ BundleVersion = $approved.bundleVersion; ArtifactHashesVerified = $true; RuntimeProbed = $false }
    return
}
if (-not $PublishRoot -or -not $Workspace -or -not [IO.Path]::IsPathRooted($Workspace) -or $Workspace.Contains('"')) {
    throw 'Runtime probe requires PublishRoot and an explicit absolute diagnostic Workspace.'
}
if (-not ('StabilityProcessJob' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'StabilityProcessJob.cs') }
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path ([IO.Path]::GetFullPath($PublishRoot)) 'TOOL-LOCAL.exe'
$mode = if ($VerifyOnly) { '--verify-piper-offline' } else { '--prepare-piper-offline' }
$start.Arguments = $mode + ' --workspace "' + [IO.Path]::GetFullPath($Workspace).TrimEnd('\') + '"'
$start.WorkingDirectory = [IO.Path]::GetTempPath()
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
# Process-only isolation: do not change machine proxy/DNS/PATH settings.
$start.EnvironmentVariables['PATH'] = [Environment]::SystemDirectory + ';' + $env:SystemRoot
$start.EnvironmentVariables['HTTP_PROXY'] = 'http://127.0.0.1:9'
$start.EnvironmentVariables['HTTPS_PROXY'] = 'http://127.0.0.1:9'
$start.EnvironmentVariables['ALL_PROXY'] = 'http://127.0.0.1:9'
$start.EnvironmentVariables['NO_PROXY'] = ''
$process = New-Object Diagnostics.Process
$process.StartInfo = $start
$job = New-Object StabilityProcessJob
try {
    if (-not $process.Start()) { throw 'Cannot start the Piper offline probe.' }
    $job.Assign($process)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(600000)) { throw 'Piper offline probe timed out.' }
    $null = $stderr.GetAwaiter().GetResult()
    $report = Read-PiperProbeReport ($stdout.GetAwaiter().GetResult())
    if ($process.ExitCode -ne 0 -or $report.PiperOfflineReady -ne $true -or $report.NetworkDownloadsAllowed -ne $false) {
        $failure = ConvertTo-PiperSafeFailureReport -Report $report -ProcessExitCode $process.ExitCode
        $failureJson = $failure | ConvertTo-Json -Depth 4 -Compress
        if ($FailureReportPath) {
            $reportStream = [IO.File]::Open([IO.Path]::GetFullPath($FailureReportPath), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                $reportBytes = [Text.Encoding]::UTF8.GetBytes($failureJson)
                $reportStream.Write($reportBytes, 0, $reportBytes.Length)
                $reportStream.Flush($true)
            } finally { $reportStream.Dispose() }
        }
        Write-Output $failureJson
        throw ('Piper offline probe failed: ' + $failure.ErrorCode)
    }
    $report
}
finally { $job.Dispose(); $process.Dispose() }
