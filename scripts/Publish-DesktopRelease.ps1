param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+(\.\d+){1,3}([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$BuildNumber,

    [ValidateSet('Stable', 'Beta', 'Development')]
    [string]$Channel = 'Stable',

    [string]$ServerBaseUrl = 'https://localhost:7202/',

    [string]$AppSettingsPath = '',

    [string]$FfmpegBundlePath = '',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$releaseRoot = Join-Path $solutionRoot ("artifacts\releases\{0}-{1}" -f $Version, $BuildNumber)
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("VideoMakerRelease\{0}" -f [Guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $workRoot 'publish'
$packageRoot = Join-Path $workRoot 'package'
$setupPublishRoot = Join-Path $workRoot 'setup'
$setupRoot = Join-Path $releaseRoot 'setup'
$resolvedFfmpegBundle = if ([string]::IsNullOrWhiteSpace($FfmpegBundlePath)) {
    [System.IO.Path]::GetFullPath((Join-Path $solutionRoot 'third_party\ffmpeg\win-x64'))
} else {
    [System.IO.Path]::GetFullPath($FfmpegBundlePath)
}

$ffmpegProfile = & (Join-Path $scriptRoot 'Test-FfmpegBundle.ps1') `
    -BundlePath $resolvedFfmpegBundle `
    -RequireReleaseApproval

if (Test-Path -LiteralPath $releaseRoot) {
    $existingFiles = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File
    if ($existingFiles.Count -gt 0) {
        throw "Release output already contains files: $releaseRoot"
    }
}

New-Item -ItemType Directory -Force -Path $releaseRoot, $publishRoot, $packageRoot, $setupPublishRoot, $setupRoot | Out-Null

try {

$desktopPublishDirectoryArgument = "-p:PublishDir=$publishRoot\"

dotnet publish (Join-Path $solutionRoot 'TOOL-LOCAL\TOOL-LOCAL.csproj') `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:DesktopBuildNumber=$BuildNumber `
    -p:PublishSingleFile=true `
    -p:RequireMediaToolBundle=true `
    ("-p:FfmpegBundleDirectory={0}" -f $resolvedFfmpegBundle) `
    $desktopPublishDirectoryArgument
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }

$publishedFfmpegProfile = & (Join-Path $scriptRoot 'Test-FfmpegBundle.ps1') `
    -BundlePath (Join-Path $publishRoot 'tools\ffmpeg') `
    -ExpectedVersion $ffmpegProfile.Version `
    -RequireReleaseApproval

$publishFiles = Get-ChildItem -LiteralPath $publishRoot -Recurse -File | Where-Object {
    $_.Extension -notin @('.pdb', '.xml') -and
    $_.FullName -notmatch '\\.WebView2([\\/]|$)' -and
    $_.Name -ne 'update-manifest.json' -and
    -not ($_.Name -eq 'VideoMaker.Updater.runtimeconfig.json' -and $_.DirectoryName -eq $publishRoot)
}

foreach ($file in $publishFiles) {
    $relative = $file.FullName.Substring($publishRoot.Length).TrimStart([char[]]@('\', '/'))
    $target = Join-Path $packageRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}

if (-not [string]::IsNullOrWhiteSpace($AppSettingsPath)) {
    $resolvedSettings = [System.IO.Path]::GetFullPath($AppSettingsPath)
    if (-not (Test-Path -LiteralPath $resolvedSettings -PathType Leaf)) { throw 'AppSettingsPath does not exist.' }
    Copy-Item -LiteralPath $resolvedSettings -Destination (Join-Path $packageRoot 'appsettings.json') -Force
}

$managedFiles = Get-ChildItem -LiteralPath $packageRoot -Recurse -File | ForEach-Object {
    $_.FullName.Substring($packageRoot.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
} | Sort-Object
$managedFiles += 'update-manifest.json'
$requiredMediaFiles = @(
    'tools/ffmpeg/ffmpeg.exe',
    'tools/ffmpeg/ffprobe.exe',
    'tools/ffmpeg/LICENSE.txt',
    'tools/ffmpeg/PROVENANCE.md',
    'tools/ffmpeg/checksums.sha256'
)
foreach ($requiredMediaFile in $requiredMediaFiles) {
    if ($requiredMediaFile -notin $managedFiles) {
        throw "Publish output hoặc update manifest thiếu media tool bắt buộc: $requiredMediaFile"
    }
}
$manifest = [ordered]@{
    product = 'VideoMaker'
    version = $Version
    buildNumber = $BuildNumber
    platform = 'win-x64'
    managedFiles = @($managedFiles | Sort-Object -Unique)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packageRoot 'update-manifest.json') -Encoding utf8

$packagePath = Join-Path $releaseRoot ("VideoMaker-{0}-{1}-win-x64.zip" -f $Version, $BuildNumber)
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $packagePath -CompressionLevel Optimal -Force
$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()

dotnet publish (Join-Path $solutionRoot 'TOOL-SETUP\TOOL-SETUP.csproj') `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:SetupServerBaseUrl=$ServerBaseUrl `
    -p:SetupChannel=$Channel `
    -p:SetupPlatform=win-x64 `
    ("-p:PublishDir={0}\" -f $setupPublishRoot)
if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }

$setupExecutable = Join-Path $setupPublishRoot 'VideoMaker Setup.exe'
if (-not (Test-Path -LiteralPath $setupExecutable -PathType Leaf)) { throw 'Setup executable was not published.' }
Copy-Item -LiteralPath $setupExecutable -Destination (Join-Path $setupRoot 'VideoMaker Setup.exe') -Force

[pscustomobject]@{
    Version = $Version
    BuildNumber = $BuildNumber
    Channel = $Channel
    Package = $packagePath
    SizeBytes = (Get-Item -LiteralPath $packagePath).Length
    Sha256 = $packageHash
    Setup = (Join-Path $setupRoot 'VideoMaker Setup.exe')
    FfmpegVersion = $publishedFfmpegProfile.Version
} | Format-List
}
finally {
    if ([System.IO.Directory]::Exists($workRoot)) {
        [System.IO.Directory]::Delete($workRoot, $true)
    }
}
