param(
    [Parameter(Mandatory = $true)]
    [string]$BundlePath,

    [string]$ExpectedVersion = '',

    [switch]$RequireReleaseApproval
)

$ErrorActionPreference = 'Stop'
$resolvedBundle = [System.IO.Path]::GetFullPath($BundlePath)
$requiredFiles = @('ffmpeg.exe', 'ffprobe.exe', 'LICENSE.txt', 'PROVENANCE.md', 'checksums.sha256')
$checksummedFiles = @('ffmpeg.exe', 'ffprobe.exe', 'LICENSE.txt')

foreach ($fileName in $requiredFiles) {
    $path = Join-Path $resolvedBundle $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "FFmpeg bundle is missing required non-empty file: $fileName at $resolvedBundle"
    }
}

function Get-ProvenanceValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $match = [regex]::Match(
        $Content,
        ('(?im)^\s*-\s*{0}:\s*(?<value>.+?)\s*$' -f [regex]::Escape($Name)))
    if (-not $match.Success) {
        throw "PROVENANCE.md is missing field '$Name'."
    }

    $value = $match.Groups['value'].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($value) -or $value.Contains('<') -or $value -match '(?i)\b(TODO|TBD)\b') {
        throw "PROVENANCE.md does not contain an approved value for '$Name'."
    }

    return $value
}

function Get-ToolVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,
        [Parameter(Mandatory = $true)]
        [string]$ToolName
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExecutablePath
    $startInfo.Arguments = '-version'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Cannot start $ToolName for its version check."
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) {
            try { $process.Kill() } catch { }
            try { $null = $process.WaitForExit(5000) } catch { }
            throw "$ToolName did not respond within 15 seconds."
        }

        $output = (($stdoutTask.GetAwaiter().GetResult() + "`n" + $stderrTask.GetAwaiter().GetResult()).Trim())
        if ($process.ExitCode -ne 0) {
            throw "$ToolName -version failed with exit code $($process.ExitCode)."
        }

        $match = [regex]::Match(
            $output,
            ('(?im)^\s*{0}\s+version\s+(?<version>\S+)' -f [regex]::Escape($ToolName)))
        if (-not $match.Success) {
            throw "Cannot parse the version returned by $ToolName -version."
        }

        return $match.Groups['version'].Value
    }
    catch [System.ComponentModel.Win32Exception] {
        throw "Windows cannot execute $ToolName. Verify win-x64 architecture and execute permissions. Details: $($_.Exception.Message)"
    }
    finally {
        $process.Dispose()
    }
}

$expectedHashes = @{}
foreach ($rawLine in Get-Content -LiteralPath (Join-Path $resolvedBundle 'checksums.sha256') -Encoding utf8) {
    $line = $rawLine.Trim()
    if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) {
        continue
    }

    $match = [regex]::Match($line, '^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<file>.+)$')
    if (-not $match.Success) {
        throw "checksums.sha256 must use the format '<sha256> *<file-name>'."
    }

    $fileName = $match.Groups['file'].Value.Trim()
    if ($fileName.Contains('/') -or $fileName.Contains('\') -or $fileName -notin $checksummedFiles) {
        throw "checksums.sha256 contains an unsupported file name: $fileName"
    }
    if ($expectedHashes.ContainsKey($fileName)) {
        throw "checksums.sha256 contains a duplicate entry: $fileName"
    }
    $expectedHashes[$fileName] = $match.Groups['hash'].Value.ToLowerInvariant()
}

foreach ($fileName in $checksummedFiles) {
    if (-not $expectedHashes.ContainsKey($fileName)) {
        throw "checksums.sha256 is missing the SHA-256 of $fileName."
    }
    $actualHash = (Get-FileHash -LiteralPath (Join-Path $resolvedBundle $fileName) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHashes[$fileName]) {
        throw "The SHA-256 of $fileName does not match the approved FFmpeg profile."
    }
}

$provenance = Get-Content -LiteralPath (Join-Path $resolvedBundle 'PROVENANCE.md') -Encoding utf8 -Raw
$profileVersion = Get-ProvenanceValue -Content $provenance -Name 'Version'
$architecture = Get-ProvenanceValue -Content $provenance -Name 'Architecture'
$null = Get-ProvenanceValue -Content $provenance -Name 'Source'
$null = Get-ProvenanceValue -Content $provenance -Name 'Acquired UTC'
$null = Get-ProvenanceValue -Content $provenance -Name 'Approved by'
$null = Get-ProvenanceValue -Content $provenance -Name 'License review'
$approvalScope = Get-ProvenanceValue -Content $provenance -Name 'Approval scope'
if ($architecture -ne 'win-x64') {
    throw "FFmpeg bundle must declare Architecture: win-x64; actual value is '$architecture'."
}
if ($approvalScope -notin @('Development', 'Release')) {
    throw "PROVENANCE.md must declare Approval scope as Development or Release; actual value is '$approvalScope'."
}
if ($RequireReleaseApproval -and $approvalScope -ne 'Release') {
    throw 'FFmpeg bundle is approved for Development only and cannot be published.'
}

$ffmpegVersion = Get-ToolVersion -ExecutablePath (Join-Path $resolvedBundle 'ffmpeg.exe') -ToolName 'ffmpeg'
$ffprobeVersion = Get-ToolVersion -ExecutablePath (Join-Path $resolvedBundle 'ffprobe.exe') -ToolName 'ffprobe'
if ($ffmpegVersion -ne $ffprobeVersion) {
    throw "ffmpeg.exe ($ffmpegVersion) and ffprobe.exe ($ffprobeVersion) are different versions."
}
if ($profileVersion -ne $ffmpegVersion) {
    throw "PROVENANCE.md declares Version '$profileVersion' but the binaries report '$ffmpegVersion'."
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $ExpectedVersion -ne $ffmpegVersion) {
    throw "Bundle version '$ffmpegVersion' does not match ExpectedVersion '$ExpectedVersion'."
}

[pscustomobject]@{
    BundlePath = $resolvedBundle
    Version = $ffmpegVersion
    FfmpegSha256 = $expectedHashes['ffmpeg.exe']
    FfprobeSha256 = $expectedHashes['ffprobe.exe']
    LicenseSha256 = $expectedHashes['LICENSE.txt']
    ApprovalScope = $approvalScope
}
