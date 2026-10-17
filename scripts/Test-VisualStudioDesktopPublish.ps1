#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AppSettingsPath,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][ValidateRange(1, 2147483647)][int]$BuildNumber,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$')][string]$ReleaseProfile,
    [Parameter(Mandatory = $true)][string]$MinimumBootstrapperVersion,
    [Parameter(Mandatory = $true)][string]$FfmpegBundlePath,
    [Parameter(Mandatory = $true)][string]$OcrNativeRuntimePath,
    [Parameter(Mandatory = $true)][string]$WebView2InstallerPath,
    [switch]$AllowTransitionalSql
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DesktopPublishedPackage.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'WebView2Prerequisite.psm1') -Force
Assert-DesktopSharedPackageVersion $Version
Assert-DesktopSharedPackageVersion $MinimumBootstrapperVersion
if ($Version -ceq '0.0.0') { throw 'A published application must have a positive version.' }

$settingsPath = Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($AppSettingsPath))
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw 'Release settings are missing. Configure FolderProfile.local.props using scripts/VisualStudioPublish.README.vi.md.'
}
try { $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { throw 'The selected release settings could not be read as JSON.' }
# Never print the JSON, a connection string or the underlying parser exception.
$api = [string]$settings.Server.BaseUrl
& (Join-Path $PSScriptRoot 'Test-DesktopDeploymentSettings.ps1') -SettingsPath $settingsPath -ExpectedServerBaseUrl $api `
    -AllowTransitionalSql:$AllowTransitionalSql -RequireTransitionalSql | Out-Null
& (Join-Path $PSScriptRoot 'Test-ModelDistributionPublishSettings.ps1') -SettingsPath $settingsPath -ModelDeliveryMode StaticArchive | Out-Null
if ($settings.Update.Channel -cne 'Stable' -or $settings.Update.Platform -cne 'win-x64') {
    throw 'Release settings must select Update.Channel=Stable and Update.Platform=win-x64.'
}

$ffmpeg = Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($FfmpegBundlePath))
$ocr = Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($OcrNativeRuntimePath))
$installer = Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($WebView2InstallerPath))
& (Join-Path $PSScriptRoot 'Test-FfmpegBundle.ps1') -BundlePath $ffmpeg -RequireReleaseApproval | Out-Null
& (Join-Path $PSScriptRoot 'Test-OcrNativeRuntime.ps1') -BundleDirectory $ocr | Out-Null
Test-ApprovedWebView2Installer -InstallerPath $installer | Out-Null
Write-Host ('VideoMaker publish inputs verified: ' + $Version + ', build ' + $BuildNumber + ', profile ' + $ReleaseProfile + '. Packaging will verify the compiled application and ZIP again.')
