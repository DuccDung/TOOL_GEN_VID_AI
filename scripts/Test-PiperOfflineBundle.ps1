param(
    [string]$BundleDirectory = '',
    [switch]$ArtifactOnly,
    [string]$PublishRoot = '',
    [string]$Workspace = '',
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
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
    $report = $stdout.GetAwaiter().GetResult() | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or $report.PiperOfflineReady -ne $true -or $report.NetworkDownloadsAllowed -ne $false) {
        $code = if ($report.ErrorCode -match '^[A-Za-z0-9_]{1,80}$') { $report.ErrorCode } else { 'invalid_report' }
        throw "Piper offline probe failed: $code"
    }
    $report
}
finally { $job.Dispose(); $process.Dispose() }
