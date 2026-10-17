#requires -Version 5.1
<#
Local ZIP qualification only. This extracts a verified application payload into
a new private QA directory and executes its own media tools. It does not run
Setup, publish a release, create shortcuts, connect to SQL or install models.
#>
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedSha256,
    [Parameter(Mandatory = $true)][string]$WorkRoot
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DesktopPublishedPackage.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'DesktopOfflinePackage.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'InstalledQualificationProcessJob.psm1') -Force
Add-Type -AssemblyName System.IO.Compression

function ConvertTo-MediaProcessArgument([string]$Value) {
    # Windows CRT quoting. Arguments never pass through a shell.
    '"' + [regex]::Replace(([regex]::Replace($Value, '(\\*)"', '$1$1\"')), '(\\+)$', '$1$1') + '"'
}

function Invoke-PackageMediaProcess {
    param([string]$Executable, [string[]]$Arguments, [string]$Directory, [int]$TimeoutSeconds = 45)
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.Arguments = (@($Arguments | ForEach-Object { ConvertTo-MediaProcessArgument $_ }) -join ' ')
    $start.WorkingDirectory = $Directory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [Text.Encoding]::UTF8
    $start.EnvironmentVariables['PATH'] = [Environment]::SystemDirectory + ';' + $env:SystemRoot
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    $job = New-InstalledQualificationProcessJob
    try {
        if (-not $process.Start()) { throw 'Cannot start the package media verification process.' }
        $job.Assign($process)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw 'Package media verification process timed out.' }
        $output = $stdout.GetAwaiter().GetResult()
        $null = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            # Native stderr and arbitrary package output are not copied into logs.
            throw 'Package media verification process returned a failure exit code.'
        }
        $output
    } finally { $job.Dispose(); $process.Dispose() }
}

$zipPath = Assert-DesktopPublishedPath $PackagePath
$work = Assert-DesktopPublishedPath $WorkRoot
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) { throw 'The application ZIP does not exist.' }
if (Test-Path -LiteralPath $work) { throw 'ZIP qualification requires a new, absent WorkRoot. Existing files were preserved.' }
if ($zipPath.StartsWith($work + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The application ZIP must be outside WorkRoot.'
}

# Keep a read-only handle while hashing and extracting. On Windows this prevents
# replacing the ZIP between the hash check and the extraction of executable bytes.
$packageHandle = [IO.File]::Open($zipPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
$archive = $null
try {
    if ($packageHandle.Length -le 0 -or $packageHandle.Length -gt 2GB) { throw 'The application ZIP exceeds release size limits.' }
    $packageBytes = $packageHandle.Length
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $actualHash = ([BitConverter]::ToString($sha.ComputeHash($packageHandle))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    if ($actualHash -cne $ExpectedSha256.ToLowerInvariant()) { throw 'Application ZIP SHA-256 does not match ExpectedSha256.' }
    $packageHandle.Position = 0
    $archive = New-Object IO.Compression.ZipArchive($packageHandle, [IO.Compression.ZipArchiveMode]::Read, $true)
    [long]$expandedBytes = 0
    if ($archive.Entries.Count -gt 200000) { throw 'The application ZIP exceeds entry limits.' }
    foreach ($entry in $archive.Entries) {
        if ($entry.Length -lt 0 -or $entry.Length -gt 4GB -or $expandedBytes -gt 4GB - $entry.Length) {
            throw 'The application ZIP exceeds expanded size limits.'
        }
        if ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000 -or ($entry.ExternalAttributes -band 0x400)) {
            throw 'The application ZIP contains a link or reparse entry.'
        }
        $expandedBytes += $entry.Length
    }
    $archive.Dispose(); $archive = $null
    Assert-DesktopPublishedFreeSpace -Path $work -RequiredBytes ($expandedBytes + 64MB)
    $applicationRoot = Join-Path $work 'application'
    $null = Expand-DesktopPackageZip -ZipPath $zipPath -DestinationRoot $applicationRoot
} finally { if ($archive) { $archive.Dispose() }; $packageHandle.Dispose() }

$report = [ordered]@{
    schemaVersion = 1; status = 'Running'; phase = 'inventory'; packageSha256 = $actualHash
    packageBytes = $packageBytes; expandedBytes = $expandedBytes
    applicationPayloadExtracted = $true; packageInventoryVerified = $false
    setupCompleted = $false; installedModelInferenceChecked = $false; cleanWindowsChecked = $false
    serverAccessChecked = $false; productionQualified = $false
}
$reportPath = Join-Path $work 'ffmpeg-package-verification.json'
try {
    $inventory = Test-DesktopPackageInventory -PackageRoot $applicationRoot
    $report.packageInventoryVerified = $true
    $report.filesVerified = $inventory.FilesVerified
    $manifest = Get-Content -LiteralPath (Join-Path $applicationRoot 'update-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $profile = Get-Content -LiteralPath (Join-Path $applicationRoot 'distribution-profile.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $report.phase = 'release-identity'
    if ($manifest.product -cne 'VideoMaker' -or $manifest.platform -cne 'win-x64' -or
        $profile.schemaVersion -ne 1 -or $profile.distributionProtocol -ne 3 -or $profile.channel -cne 'Stable' -or
        $profile.platform -cne 'win-x64' -or $profile.modelBundleId -cne 'models-20261016-03' -or
        $profile.profile -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9._-]{0,49}\z') {
        throw 'ZIP metadata does not describe a compatible SharedVps application release.'
    }
    $managed = @($manifest.managedFiles)
    $files = @(Get-DesktopPublishedFiles $applicationRoot)
    $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $managed) { if (-not $seen.Add([string]$path)) { throw 'Update manifest contains duplicate owned paths.' } }
    if ($managed.Count -ne $files.Count -or @($files | Where-Object { -not $seen.Contains($_.Path) }).Count) {
        throw 'Update manifest does not own exactly the application ZIP payload.'
    }
    $null = Test-DesktopPublishedIdentity -PublishRoot $applicationRoot -Version $manifest.version -BuildNumber $manifest.buildNumber
    $report.version = [string]$manifest.version
    $report.buildNumber = [int]$manifest.buildNumber
    $report.phase = 'compiled-policy'
    $nativePolicy = Invoke-PackageMediaProcess -Executable (Join-Path $applicationRoot 'TOOL-LOCAL.exe') `
        -Arguments @('--check-static-model-policy') -Directory $work | ConvertFrom-Json
    if ($nativePolicy.required -ne $true -or $nativePolicy.BundleId -cne $profile.modelBundleId -or
        $nativePolicy.ArchiveSha256 -cne 'e9939863910af178ec6be6e99f7fcc16948b6a8d916bd5b32bc0a50d92d3f471' -or
        $nativePolicy.version -cne $manifest.version -or $nativePolicy.buildNumber -ne $manifest.buildNumber -or
        $nativePolicy.distributionProtocol -ne 3 -or $nativePolicy.profile -cne $profile.profile -or
        $nativePolicy.channel -cne 'Stable' -or $nativePolicy.downloadPolicyVersion -ne 1 -or $nativePolicy.ModelUseAllowed -ne $true) {
        throw 'The native application policy does not match the application ZIP release.'
    }
    $report.compiledSharedPolicyVerified = $true
    $report.phase = 'ffmpeg-bundle'
    $mediaRoot = Join-Path $applicationRoot 'tools/ffmpeg'
    $media = & (Join-Path $PSScriptRoot 'Test-FfmpegBundle.ps1') -BundlePath $mediaRoot -RequireReleaseApproval
    $report.ffmpegVersion = $media.Version
    $report.ffmpegSha256 = $media.FfmpegSha256
    $report.ffprobeSha256 = $media.FfprobeSha256
    $report.approvalScope = $media.ApprovalScope
    $ffmpeg = Join-Path $mediaRoot 'ffmpeg.exe'
    $ffprobe = Join-Path $mediaRoot 'ffprobe.exe'
    $smoke = Join-Path $work 'media-smoke'
    [IO.Directory]::CreateDirectory($smoke) | Out-Null
    # Numeric character construction keeps Windows PowerShell 5.1 compatible even
    # when this source file is read as ANSI; the resulting fixture is UTF-8.
    $subtitle = 'VideoMaker: Ph' + [char]0x1EE5 + ' ' + [char]0x0111 + [char]0x1EC1 + ' Vi' + [char]0x1EC7 + 't Nam'
    [IO.File]::WriteAllText((Join-Path $smoke 'subtitles.srt'), "1`r`n00:00:00,000 --> 00:00:00,900`r`n$subtitle`r`n", [Text.UTF8Encoding]::new($false))
    $report.phase = 'h264-aac-render'
    $null = Invoke-PackageMediaProcess $ffmpeg @('-hide_banner', '-nostdin', '-v', 'error', '-f', 'lavfi', '-i',
        'color=c=black:s=320x180:r=24:d=1', '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000:duration=1',
        '-map', '0:v:0', '-map', '1:a:0', '-c:v', 'libx264', '-threads', '1', '-pix_fmt', 'yuv420p',
        '-c:a', 'aac', '-b:a', '64k', '-t', '1', '-movflags', '+faststart', 'source.mp4') $smoke
    $report.phase = 'subtitle-render'
    $null = Invoke-PackageMediaProcess $ffmpeg @('-hide_banner', '-nostdin', '-v', 'error', '-i', 'source.mp4', '-vf',
        "subtitles=subtitles.srt:force_style='FontName=Arial,FontSize=20,Alignment=2'", '-c:v', 'libx264', '-threads', '1',
        '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-t', '1', 'burned.mp4') $smoke
    $null = Invoke-PackageMediaProcess $ffmpeg @('-hide_banner', '-nostdin', '-v', 'error', '-i', 'burned.mp4', '-i',
        'subtitles.srt', '-map', '0:v:0', '-map', '0:a:0', '-map', '1:0', '-c:v', 'copy', '-c:a', 'copy',
        '-c:s', 'mov_text', '-metadata:s:s:0', 'language=vie', 'subtitled.mp4') $smoke
    $report.phase = 'ffprobe-streams'
    $probe = Invoke-PackageMediaProcess $ffprobe @('-v', 'error', '-show_streams', '-show_format', '-of', 'json', 'subtitled.mp4') $smoke | ConvertFrom-Json
    $video = @($probe.streams | Where-Object { $_.codec_type -eq 'video' })
    $audio = @($probe.streams | Where-Object { $_.codec_type -eq 'audio' })
    $text = @($probe.streams | Where-Object { $_.codec_type -eq 'subtitle' })
    $duration = [double]::Parse([string]$probe.format.duration, [Globalization.CultureInfo]::InvariantCulture)
    if ($video.Count -ne 1 -or $video[0].codec_name -cne 'h264' -or $video[0].width -ne 320 -or $video[0].height -ne 180 -or
        $audio.Count -ne 1 -or $audio[0].codec_name -cne 'aac' -or $text.Count -ne 1 -or $text[0].codec_name -cne 'mov_text' -or
        $duration -lt 0.9 -or $duration -gt 1.2) { throw 'FFprobe did not verify the expected H264/AAC/subtitle streams and duration.' }
    $null = Invoke-PackageMediaProcess $ffmpeg @('-hide_banner', '-nostdin', '-v', 'error', '-i', 'subtitled.mp4',
        '-map', '0:s:0', '-c:s', 'srt', 'roundtrip.srt') $smoke
    if (-not ([IO.File]::ReadAllText((Join-Path $smoke 'roundtrip.srt'), [Text.Encoding]::UTF8)).Contains($subtitle)) {
        throw 'Vietnamese subtitles did not survive the MP4 subtitle round trip.'
    }
    $report.phase = 'subtitle-pixels'
    foreach ($name in @('source', 'burned')) {
        $null = Invoke-PackageMediaProcess $ffmpeg @('-hide_banner', '-nostdin', '-v', 'error', '-ss', '0.5', '-i', ($name + '.mp4'),
            '-frames:v', '1', '-vf', 'format=gray', '-f', 'rawvideo', ($name + '.gray')) $smoke
    }
    $before = [IO.File]::ReadAllBytes((Join-Path $smoke 'source.gray'))
    $after = [IO.File]::ReadAllBytes((Join-Path $smoke 'burned.gray'))
    if ($before.Length -ne 57600 -or $after.Length -ne $before.Length) { throw 'Unexpected subtitle frame dimensions.' }
    $changedPixels = 0
    for ($index = 0; $index -lt $before.Length; $index++) {
        if ([Math]::Abs([int]$before[$index] - [int]$after[$index]) -ge 24) { $changedPixels++ }
    }
    if ($changedPixels -lt 100) { throw 'Rendered subtitles did not create visible image pixels.' }
    $report.subtitleChangedPixels = $changedPixels
    $report.vietnameseSubtitleRoundTrip = $true
    $report.h264AacSubtitleRender = $true
    $report.mediaOutputSha256 = (Get-FileHash -LiteralPath (Join-Path $smoke 'subtitled.mp4') -Algorithm SHA256).Hash.ToLowerInvariant()
    # Recheck the untouched application payload after executing the tools.
    $null = Test-DesktopPackageInventory -PackageRoot $applicationRoot
    $report.phase = 'complete'
    $report.status = 'Passed'
} catch {
    $report.status = 'Failed'
    throw
} finally {
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}
[pscustomobject]$report
