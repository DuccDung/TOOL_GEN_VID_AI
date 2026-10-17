param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+(\.\d+){1,3}([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [Parameter(Mandatory = $true)][ValidateRange(1, 2147483647)][int]$BuildNumber,
    [string]$OutputRoot = '',
    [string]$QwenModelPath = '',
    [string]$AppSettingsPath = '',
    [switch]$SkipQwenRuntimeProbe,
    [switch]$AcceptResourceWarning,
    [ValidateRange(1, 16)][int]$BuildMaxCpuCount = 1
)

# Development qualification only. This never promotes a Release approval or
# produces an installer for a customer environment that has not been specified.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'DesktopOfflinePackage.psm1') -Force
$qwen = Get-DesktopQwenDefinition
if (-not $QwenModelPath) { $QwenModelPath = Join-Path $repo $qwen.RelativePath }
Test-DesktopQwenModel $QwenModelPath | Out-Null
& (Join-Path $PSScriptRoot 'Test-PiperOfflineBundle.ps1') -ArtifactOnly | Out-Null
& (Join-Path $PSScriptRoot 'Test-OcrNativeRuntime.ps1') -BundleDirectory (Join-Path $repo 'artifacts/ocr-native-runtime/msvc-14.50.35719.0-win-x64') | Out-Null
$ffmpeg = & (Join-Path $PSScriptRoot 'Test-FfmpegBundle.ps1') -BundlePath (Join-Path $repo 'third_party/ffmpeg/win-x64')
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo ("artifacts/offline-candidates/{0}-{1}" -f $Version, $BuildNumber) }
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw 'Candidate output must be a new directory.' }
for ($folder = New-Object IO.DirectoryInfo $output; $null -ne $folder; $folder = $folder.Parent) {
    if ($folder.Exists -and ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe candidate output path.' }
}
[IO.Directory]::CreateDirectory($output) | Out-Null
$publish = Join-Path $output 'publish'
$package = Join-Path $output 'package'
$extracted = Join-Path $output 'extracted có dấu'
$settings = Join-Path $output 'diagnostic-settings.json'
if ($AppSettingsPath) {
    $AppSettingsPath = [IO.Path]::GetFullPath($AppSettingsPath)
    $deployment = Get-Content -LiteralPath $AppSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    & (Join-Path $PSScriptRoot 'Test-DesktopDeploymentSettings.ps1') -SettingsPath $AppSettingsPath `
        -ExpectedServerBaseUrl ([string]$deployment.Server.BaseUrl) -AllowTransitionalSql | Out-Null
    Copy-Item -LiteralPath $AppSettingsPath -Destination $settings
} else { [IO.File]::WriteAllText($settings, '{}') }
$sourceCommit = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the candidate source commit.' }
$sourceFiles = @(& git -C $repo ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inventory the candidate source.' }
$sourceInventory = foreach ($relative in $sourceFiles) {
    $path = Join-Path $repo $relative
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        [ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
$sourceInventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'source-inventory.json') -Encoding UTF8

dotnet publish (Join-Path $repo 'TOOL-LOCAL/TOOL-LOCAL.csproj') -c Release -r win-x64 --self-contained true `
    "-m:$BuildMaxCpuCount" -p:BuildInParallel=false -p:UseSharedCompilation=false `
    "-p:Version=$Version" "-p:DesktopBuildNumber=$BuildNumber" -p:PublishSingleFile=true `
    -p:RequireMediaToolBundle=true -p:RequirePiperOfflineBundle=true `
    "-p:DesktopDeploymentSettingsPath=$settings" "-p:PublishDir=$publish\" > (Join-Path $output 'publish.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Candidate desktop publish failed. See the isolated publish log.' }
[IO.Directory]::CreateDirectory($package) | Out-Null
foreach ($file in @(Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object {
    $_.Extension -notin @('.pdb', '.xml') -and $_.Name -ne 'update-manifest.json' -and
    $_.FullName -notmatch '\\.WebView2([\\/]|$)' -and
    -not ($_.Name -eq 'VideoMaker.Updater.runtimeconfig.json' -and $_.DirectoryName -eq $publish)
})) {
    $relative = $file.FullName.Substring($publish.Length).TrimStart('\', '/')
    $target = Join-Path $package $relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
Add-DesktopQwenModel -ModelPath $QwenModelPath -PackageRoot $package | Out-Null
foreach ($extra in @('Configure-CustomerSql.ps1', 'Check-Qwen-Customer.ps1')) {
    $path = Join-Path $PSScriptRoot $extra
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $package $extra) }
}
@'
DEVELOPMENT DIAGNOSTIC CANDIDATE - NOT A CUSTOMER RELEASE

Includes OCR en/zh, Qwen3 4B CPU and Piper offline payloads.
appsettings.json is intentionally empty: login and SQL deployment are not configured.
Use diagnostic commands only. The release script still requires customer settings
and Release-approved FFmpeg. This ZIP has not passed customer Startup/UI smoke.
Do not upload this candidate as an Active desktop release.
'@ | Set-Content -LiteralPath (Join-Path $package 'CANDIDATE-README.txt') -Encoding UTF8
if ($AppSettingsPath) {
    @'
OFFLINE SUPPORT CANDIDATE - QUALIFICATION REQUIRED BEFORE CUSTOMER RELEASE

Includes FFmpeg/FFprobe, PaddleOCR en/zh, Qwen3 4B CPU and Piper offline payloads.
Each isolated worker includes its own pinned native runtime.
The included settings target the selected HTTPS server. SQL still needs a private
per-client configuration if Database.ConnectionString is empty; this is not API-only.
Use Configure-CustomerSql.ps1 on the recipient's own machine, then launch TOOL-LOCAL.exe.
Never redistribute appsettings.user.json, account tokens or READY markers.
See the qualification report accompanying this ZIP for probes and release gates.
Preserved FFmpeg provenance is not automatically promoted to Release approval.
Do not upload this candidate as an Active desktop release.
'@ | Set-Content -LiteralPath (Join-Path $package 'CANDIDATE-README.txt') -Encoding UTF8
}
$managedFiles = @(Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object {
    $_.FullName.Substring($package.Length).TrimStart('\', '/').Replace('\', '/')
}) + @('update-manifest.json', 'package-inventory.json')
[ordered]@{ product = 'VideoMaker'; version = $Version; buildNumber = $BuildNumber; platform = 'win-x64';
    managedFiles = @($managedFiles | Sort-Object -Unique) } | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $package 'update-manifest.json') -Encoding UTF8
New-DesktopPackageInventory $package
$zip = Join-Path $output ("VideoMaker-{0}-{1}-Qwen-Piper-DIAGNOSTIC.zip" -f $Version, $BuildNumber)
New-DesktopPackageZip -PackageRoot $package -DestinationPath $zip
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
('{0}  {1}' -f $zipHash, [IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath (Join-Path $output 'checksums.sha256') -Encoding ASCII
$extraction = Expand-DesktopPackageZip -ZipPath $zip -DestinationRoot $extracted
$inventoryReport = Test-DesktopPackageInventory $extracted
$componentsReport = & (Join-Path $PSScriptRoot 'Test-DesktopSetupPublish.ps1') -PublishRoot $extracted `
    -ProbeWebView2 -ProbeBundledComponents -ProbePiperOffline -RequireQwenModel
$qwenReport = if ($SkipQwenRuntimeProbe) {
    [pscustomobject]@{ QwenOfflineReady = $false; RuntimeProbed = $false; Stage = 'NOT_RUN'; ErrorCode = 'qualification_pending' }
} else {
    & (Join-Path $PSScriptRoot 'Test-QwenOfflineBundle.ps1') -PublishRoot $extracted -AcceptResourceWarning:$AcceptResourceWarning
}
$report = [ordered]@{
    Status = 'DevelopmentCandidate'; SourceCommit = $sourceCommit; SourceWorkingTreeInventoried = $true
    Version = $Version; BuildNumber = $BuildNumber; Package = [IO.Path]::GetFileName($zip)
    SizeBytes = (Get-Item -LiteralPath $zip).Length; Sha256 = $zipHash; ExtractedBytes = $extraction.ExtractedBytes
    ServerUploadCompatibleAtDefaultLimit = (Get-Item -LiteralPath $zip).Length -le 2147483648
    Inventory = $inventoryReport; Components = $componentsReport; Qwen = $qwenReport
    FfmpegVersion = $ffmpeg.Version; FfmpegReleaseApproved = $ffmpeg.ApprovalScope -eq 'Release'
    DeploymentSettingsIncluded = [bool]$AppSettingsPath
    CustomerSettingsConfigured = [bool]($AppSettingsPath -and $deployment.Database.ConnectionString)
    SqlProvisioningRequired = -not [bool]($AppSettingsPath -and $deployment.Database.ConnectionString)
    CustomerStartupChecked = $false
    ProductionRolloutVerified = $false
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'qualification.json') -Encoding UTF8
[pscustomobject]$report
