#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishRoot,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][ValidateRange(1, 2147483647)][int]$BuildNumber,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [string]$PublishFileListPath = '',
    [switch]$AllowTransitionalSql
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DesktopCanonicalPackage.psm1') -Force -DisableNameChecking
$settingsPath = Join-Path ([IO.Path]::GetFullPath($PublishRoot)) 'appsettings.json'
try { $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { throw 'The published desktop configuration could not be read as JSON.' }
& (Join-Path $PSScriptRoot 'Test-DesktopDeploymentSettings.ps1') -SettingsPath $settingsPath -ExpectedServerBaseUrl ([string]$settings.Server.BaseUrl) `
    -AllowTransitionalSql:$AllowTransitionalSql -RequireTransitionalSql | Out-Null
& (Join-Path $PSScriptRoot 'Test-ModelDistributionPublishSettings.ps1') -SettingsPath $settingsPath -ModelDeliveryMode StaticArchive | Out-Null
$result = New-DesktopCanonicalPackage -PublishRoot $PublishRoot -Version $Version -BuildNumber $BuildNumber `
    -OutputRoot $OutputRoot -PublishFileListPath $PublishFileListPath
Write-Output ('VideoMaker ZIP: ' + $result.PackagePath)
Write-Output ('Upload this ZIP unchanged, then enter version ' + $result.Version + ' and its HTTPS URL in Admin. Shared Setup 1.2.0 or newer verifies and installs it.')
