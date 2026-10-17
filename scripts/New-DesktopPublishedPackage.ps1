#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishRoot,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][ValidateRange(1, 2147483647)][int]$BuildNumber,
    [string]$ServerBaseUrl = '',
    [string]$OutputRoot = '',
    [ValidateSet('Stable')][string]$Channel = 'Stable',
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$')][string]$ReleaseProfile = 'Production',
    [string]$MinimumBootstrapperVersion = '1.0.0',
    [string]$AppSettingsPath = '',
    [switch]$AllowTransitionalSql,
    [string]$WebView2InstallerPath = '',
    [string]$PublishFileListPath = '',
    [string]$SourceWebRoot = '',
    [ValidateSet('None', 'WindowsStore')][string]$AuthenticodeMode = 'None',
    [string]$SigningCertificateThumbprint = '',
    [ValidateSet('CurrentUser', 'LocalMachine')][string]$SigningCertificateStoreLocation = 'CurrentUser',
    [string]$SignToolPath = '',
    [string]$SigningTimestampUrl = '',
    [switch]$RequireAuthenticode
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $PSScriptRoot '..\artifacts\releases' }
if ([string]::IsNullOrWhiteSpace($SourceWebRoot)) { $SourceWebRoot = Join-Path $PSScriptRoot '..\TOOL-LOCAL\Web\dist' }
Import-Module (Join-Path $PSScriptRoot 'DesktopPublishedPackage.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'DesktopOfflinePackage.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'DesktopExternalRelease.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'DesktopAuthenticode.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'WebView2Prerequisite.psm1') -Force

Assert-DesktopSharedPackageVersion $Version
Assert-DesktopSharedPackageVersion $MinimumBootstrapperVersion
$source = Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($PublishRoot))
$output = Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($OutputRoot))
$webSource = Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($SourceWebRoot))
if ($output -ieq $source -or $output.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $source.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The release output and Visual Studio publish directory must be separate.'
}
# Snapshot and validate the tree before any compiled command is started. Walk one
# directory at a time so a junction is rejected before entering its target.
$null = @(Get-DesktopPublishedFiles $source)
$null = @(Get-DesktopPublishedFiles $webSource)
Test-DesktopPublishedIdentity -PublishRoot $source -Version $Version -BuildNumber $BuildNumber | Out-Null
$settingsPath = if ($AppSettingsPath) { Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($AppSettingsPath)) } else { Join-Path $source 'appsettings.json' }
try { $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { throw 'The desktop deployment configuration could not be read as JSON.' }
$api = Test-DesktopCustomerApiUrl $(if ($ServerBaseUrl) { $ServerBaseUrl } else { [string]$settings.Server.BaseUrl })
& (Join-Path $PSScriptRoot 'Test-DesktopDeploymentSettings.ps1') -SettingsPath $settingsPath -ExpectedServerBaseUrl $api `
    -AllowTransitionalSql:$AllowTransitionalSql -RequireTransitionalSql | Out-Null
& (Join-Path $PSScriptRoot 'Test-ModelDistributionPublishSettings.ps1') -SettingsPath $settingsPath -ModelDeliveryMode StaticArchive | Out-Null
if ($settings.Update.Channel -cne $Channel -or $settings.Update.Platform -cne 'win-x64') {
    throw 'Desktop update channel/platform does not match Stable/win-x64.'
}
& (Join-Path $PSScriptRoot 'Test-FfmpegBundle.ps1') -BundlePath (Join-Path $source 'tools/ffmpeg') -RequireReleaseApproval | Out-Null
$webViewPath = if ($WebView2InstallerPath) { Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($WebView2InstallerPath)) } else { Join-Path $source 'prerequisites/webview2-runtime-x64.exe' }
$webView = Test-ApprovedWebView2Installer -InstallerPath $webViewPath
$policy = New-DesktopAuthenticodePolicy -Mode $AuthenticodeMode -CertificateThumbprint $SigningCertificateThumbprint `
    -CertificateStoreLocation $SigningCertificateStoreLocation -SignToolPath $SignToolPath -TimestampUrl $SigningTimestampUrl -RequireAuthenticode:$RequireAuthenticode
$downloadPolicy = Get-DesktopExternalDownloadPolicy
[IO.Directory]::CreateDirectory($output) | Out-Null
$runId = [Guid]::NewGuid().ToString('N')
$workRoot = Join-Path $output ('.vm-package-work-' + $runId + '.part')
$stagingRoot = Join-Path $output ('.vm-package-' + $runId + '.part')
$packageRoot = Join-Path $output ('.vm-package-payload-' + $runId + '.part')
$extractedRoot = Join-Path $workRoot 'extracted'
$archiveName = 'VideoMaker-' + $Version + '-' + $BuildNumber + '-win-x64.zip'
$archivePart = Join-Path $stagingRoot ($archiveName + '.part')
$archivePath = Join-Path $stagingRoot $archiveName
try {
    [IO.Directory]::CreateDirectory($workRoot) | Out-Null
    [IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
    Copy-DesktopPublishedPayload -PublishRoot $source -PackageRoot $packageRoot -PublishFileListPath $PublishFileListPath | Out-Null
    Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $packageRoot 'appsettings.json') -Force
    $prerequisiteRoot = Join-Path $packageRoot 'prerequisites'
    [IO.Directory]::CreateDirectory($prerequisiteRoot) | Out-Null
    Copy-Item -LiteralPath $webView.Path -Destination (Join-Path $prerequisiteRoot 'webview2-runtime-x64.exe') -Force
    foreach ($entry in @(@('PROVENANCE.md', 'WEBVIEW2-PROVENANCE.md'), @('WEBVIEW2_INSTALLER_APPROVED.json', 'WEBVIEW2_INSTALLER_APPROVED.json'))) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('../third_party/webview2/' + $entry[0])) -Destination (Join-Path $prerequisiteRoot $entry[1]) -Force
    }
    Test-ApprovedWebView2Installer -InstallerPath (Join-Path $prerequisiteRoot 'webview2-runtime-x64.exe') | Out-Null
    $signing = Invoke-DesktopAuthenticodeSigning -Root $packageRoot -Kind Desktop -Policy $policy
    Test-DesktopPublishedIdentity -PublishRoot $packageRoot -Version $Version -BuildNumber $BuildNumber | Out-Null
    & (Join-Path $PSScriptRoot 'Test-DesktopDeploymentSettings.ps1') -SettingsPath (Join-Path $packageRoot 'appsettings.json') -ExpectedServerBaseUrl $api `
        -AllowTransitionalSql:$AllowTransitionalSql -RequireTransitionalSql | Out-Null
    $beforeZip = & (Join-Path $PSScriptRoot 'Test-DesktopSetupPublish.ps1') -PublishRoot $packageRoot -SourceWebRoot $webSource -ProbeWebView2 `
        -ExpectedModelDeliveryMode StaticArchive -ExpectedVersion $Version -ExpectedBuildNumber $BuildNumber -ExpectedChannel $Channel `
        -ExpectedDistributionProtocol 3 -ExpectedProfile $ReleaseProfile
    New-DesktopSharedPackageMetadata -PackageRoot $packageRoot -Version $Version -BuildNumber $BuildNumber -ServerBaseUrl $api `
        -ReleaseProfile $ReleaseProfile -MinimumBootstrapperVersion $MinimumBootstrapperVersion
    $expandedSize = [long](@(Get-DesktopPublishedFiles $packageRoot) | Measure-Object SizeBytes -Sum).Sum
    # ZIP creation and extraction occur sequentially. Reserve enough for a ZIP
    # even for incompressible input, then release only our verified payload.
    Assert-DesktopPublishedFreeSpace -Path $output -RequiredBytes ($expandedSize + [long]134217728)
    New-DesktopPackageZip -PackageRoot $packageRoot -DestinationPath $archivePart
    $size = (Get-Item -LiteralPath $archivePart).Length
    if ($size -lt 4 -or $size -gt $downloadPolicy.MaximumArchiveBytes) { throw 'Application ZIP exceeds the approved archive size policy.' }
    # Even the temporary ZIP is read through the same manifest/policy validator
    # used for release registration. The filename needs its final .zip suffix.
    [IO.File]::Move($archivePart, $archivePath)
    $zip = Get-DesktopRegistrationPackage -PackagePath $archivePath -Version $Version -BuildNumber $BuildNumber
    Remove-DesktopVerifiedPackagePayload -PayloadRoot $packageRoot -OutputRoot $output -ArchivePath $archivePath `
        -Version $Version -BuildNumber $BuildNumber -ExpectedSha256 $zip.Sha256 | Out-Null
    Assert-DesktopPublishedFreeSpace -Path $output -RequiredBytes ($zip.ExpandedSizeBytes + [long]134217728)
    $extraction = Expand-DesktopPackageZip -ZipPath $archivePath -DestinationRoot $extractedRoot
    Test-DesktopPackageInventory -PackageRoot $extractedRoot | Out-Null
    Test-DesktopAuthenticodeSignatures -Root $extractedRoot -Kind Desktop -Policy $policy | Out-Null
    Test-DesktopPublishedIdentity -PublishRoot $extractedRoot -Version $Version -BuildNumber $BuildNumber | Out-Null
    & (Join-Path $PSScriptRoot 'Test-FfmpegBundle.ps1') -BundlePath (Join-Path $extractedRoot 'tools/ffmpeg') -RequireReleaseApproval | Out-Null
    & (Join-Path $PSScriptRoot 'Test-DesktopDeploymentSettings.ps1') -SettingsPath (Join-Path $extractedRoot 'appsettings.json') -ExpectedServerBaseUrl $api `
        -AllowTransitionalSql:$AllowTransitionalSql -RequireTransitionalSql | Out-Null
    $verified = & (Join-Path $PSScriptRoot 'Test-DesktopSetupPublish.ps1') -PublishRoot $extractedRoot -SourceWebRoot $webSource -ProbeWebView2 -ProbeBundledComponents `
        -ExpectedModelDeliveryMode StaticArchive -ExpectedVersion $Version -ExpectedBuildNumber $BuildNumber -ExpectedChannel $Channel `
        -ExpectedDistributionProtocol 3 -ExpectedProfile $ReleaseProfile
    if ($extraction.ExtractedBytes -ne $zip.ExpandedSizeBytes -or $extraction.Entries -ne $zip.EntryCount) { throw 'Extracted ZIP no longer matches its verified structure.' }
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -cne $zip.Sha256 -or (Get-Item -LiteralPath $archivePath).Length -ne $size) { throw 'Application ZIP changed during final verification.' }
    ($hash + '  ' + $archiveName) | Set-Content -LiteralPath (Join-Path $stagingRoot 'checksums.sha256') -Encoding ASCII
    [ordered]@{
        schemaVersion = 1; deliveryMode = 'SharedVps'; version = $Version; buildNumber = $BuildNumber
        package = $archiveName; sizeBytes = $size; sha256 = $hash; expandedSizeBytes = $zip.ExpandedSizeBytes; entryCount = $zip.EntryCount
        profile = $ReleaseProfile; minimumBootstrapperVersion = $MinimumBootstrapperVersion
        setupBuilt = $false; productionQualified = $false; packageVerified = $true; nativeIdentityVerified = $true
        webView2State = $verified.WebView2State; bundledComponentsState = $verified.BundledComponentsState
        installedModelInferenceChecked = $false; cleanWindowsInstallChecked = $false; authenticodeSigned = $signing.Signed
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stagingRoot 'package-report.json') -Encoding UTF8
    $result = Complete-DesktopPublishedPackage -StagingRoot $stagingRoot -OutputRoot $output -Version $Version -BuildNumber $BuildNumber
    Write-Host ('Upload this application ZIP to the VPS: ' + $result.Package)
    $result
} finally {
    if (Test-Path -LiteralPath $packageRoot) {
        Remove-DesktopOwnedPackagingDirectory -Path $packageRoot -Parent $output -NamePattern '^\.vm-package-payload-[0-9a-f]{32}\.part$'
    }
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-DesktopOwnedPackagingDirectory -Path $stagingRoot -Parent $output -NamePattern '^\.vm-package-[0-9a-f]{32}\.part$'
    }
    if (Test-Path -LiteralPath $workRoot) {
        Remove-DesktopOwnedPackagingDirectory -Path $workRoot -Parent $output -NamePattern '^\.vm-package-work-[0-9a-f]{32}\.part$'
    }
}
