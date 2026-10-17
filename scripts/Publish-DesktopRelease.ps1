param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+(\.\d+){1,3}([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$BuildNumber,

    [ValidateSet('Stable', 'Beta', 'Development')]
    [string]$Channel = 'Stable',

    [Parameter(Mandatory = $true)]
    [string]$ServerBaseUrl,

    [string]$AppSettingsPath = '',

    [switch]$AllowTransitionalSql,

    [string]$FfmpegBundlePath = '',
    [string]$PiperBundlePath = '',
    [string]$OcrNativeRuntimePath = '',

    [switch]$IncludeQwenModel,
    [switch]$AcceptQwenResourceWarning,
    [string]$QwenModelPath = '',
    [ValidateSet('Bundled', 'Server', 'StaticArchive')]
    [string]$ModelDeliveryMode = 'Bundled',
    [string]$ModelDistributionTrustRootPath = '',
    [string]$WebView2InstallerPath = '',
    [ValidateSet('None', 'WindowsStore')]
    [string]$AuthenticodeMode = 'None',
    [string]$SigningCertificateThumbprint = '',
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$SigningCertificateStoreLocation = 'CurrentUser',
    [string]$SignToolPath = '',
    [string]$SigningTimestampUrl = '',
    [switch]$RequireAuthenticode,
    [ValidateSet('Updater', 'Direct', 'ExternalVps', 'SharedVps')]
    [string]$DeliveryMode = 'Updater',
    [string]$PackageDownloadUrl = '',
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$')]
    [string]$ReleaseProfile = 'Production',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$MinimumBootstrapperVersion = '1.0.0',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
function Assert-SharedDesktopPublishInput {
    param([string]$Channel, [string[]]$Versions)
    if ($Channel -cne 'Stable') { throw 'Shared desktop distribution supports only the Stable channel.' }
    foreach ($value in $Versions) {
        if ($null -eq $value -or $value.Length -gt 32 -or $value -cnotmatch '\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\z') {
            throw 'Shared desktop versions must use canonical major.minor.patch without leading zeroes.'
        }
        foreach ($part in $value.Split('.')) {
            $number = 0
            if (-not [int]::TryParse($part, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
                throw 'Shared desktop version components must fit Int32.'
            }
        }
    }
}
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
Import-Module (Join-Path $scriptRoot 'DesktopOfflinePackage.psm1') -Force
Import-Module (Join-Path $scriptRoot 'DesktopAuthenticode.psm1') -Force
Import-Module (Join-Path $scriptRoot 'DesktopExternalRelease.psm1') -Force
if ($DeliveryMode -eq 'SharedVps') {
    if ($ModelDeliveryMode -ne 'StaticArchive') { throw 'SharedVps delivery requires StaticArchive and a shared system Setup.' }
    Assert-SharedDesktopPublishInput -Channel $Channel -Versions @($Version, $MinimumBootstrapperVersion)
    if ($Version -ceq '0.0.0') { throw 'SharedVps requires a positive application version.' }
    $null = Test-DesktopCustomerApiUrl $ServerBaseUrl
    if ($PackageDownloadUrl) { $PackageDownloadUrl = Test-DesktopExternalDownloadUrl $PackageDownloadUrl }
} elseif ($DeliveryMode -eq 'ExternalVps') {
    if ($ModelDeliveryMode -ne 'StaticArchive') { throw 'ExternalVps delivery requires StaticArchive and its exact bound Setup.' }
    if ([string]::IsNullOrWhiteSpace($PackageDownloadUrl)) { throw 'ExternalVps requires -PackageDownloadUrl for the immutable application ZIP on the VPS.' }
    $PackageDownloadUrl = Test-DesktopExternalDownloadUrl $PackageDownloadUrl
    $null = Test-DesktopCustomerApiUrl $ServerBaseUrl
} elseif ($PackageDownloadUrl) { throw 'PackageDownloadUrl is supported only with ExternalVps delivery.' }
$authenticodePolicy = New-DesktopAuthenticodePolicy -Mode $AuthenticodeMode `
    -CertificateThumbprint $SigningCertificateThumbprint -CertificateStoreLocation $SigningCertificateStoreLocation `
    -SignToolPath $SignToolPath -TimestampUrl $SigningTimestampUrl -RequireAuthenticode:$RequireAuthenticode
$releaseRoot = Join-Path $solutionRoot ("artifacts\releases\{0}-{1}" -f $Version, $BuildNumber)
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("VideoMakerRelease\{0}" -f [Guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $workRoot 'publish'
$packageRoot = Join-Path $workRoot 'package'
$setupPublishRoot = Join-Path $workRoot 'setup'
$setupRoot = Join-Path $releaseRoot 'setup'
$resolvedSettings = if ([string]::IsNullOrWhiteSpace($AppSettingsPath)) {
    Join-Path $solutionRoot 'TOOL-LOCAL\appsettings.json'
} else {
    [System.IO.Path]::GetFullPath($AppSettingsPath)
}
& (Join-Path $scriptRoot 'Test-DesktopDeploymentSettings.ps1') `
    -SettingsPath $resolvedSettings -ExpectedServerBaseUrl $ServerBaseUrl `
    -AllowTransitionalSql:$AllowTransitionalSql -RequireTransitionalSql | Out-Null
$releaseSettings = Get-Content -LiteralPath $resolvedSettings -Raw -Encoding UTF8 | ConvertFrom-Json
$resolvedModelTrustRoot = if ($ModelDistributionTrustRootPath) { [IO.Path]::GetFullPath($ModelDistributionTrustRootPath) } else { '' }
$modelPolicy = & (Join-Path $scriptRoot 'Test-ModelDistributionPublishSettings.ps1') -SettingsPath $resolvedSettings `
    -ModelDeliveryMode $ModelDeliveryMode -TrustRootPath $resolvedModelTrustRoot -IncludeQwenModel:$IncludeQwenModel `
    -QwenModelPath $QwenModelPath -PiperBundlePath $PiperBundlePath
$webView2Installer = $null
if ($ModelDeliveryMode -eq 'StaticArchive') {
    if (-not $WebView2InstallerPath) { throw 'StaticArchive requires the approved offline WebView2 installer via -WebView2InstallerPath.' }
    Import-Module (Join-Path $scriptRoot 'WebView2Prerequisite.psm1') -Force
    $webView2Installer = Test-ApprovedWebView2Installer -InstallerPath $WebView2InstallerPath
} elseif ($WebView2InstallerPath) {
    throw 'WebView2InstallerPath is supported only for StaticArchive releases.'
}
if ($IncludeQwenModel) {
    $qwenDefinition = Get-DesktopQwenDefinition
    if (-not $QwenModelPath) { $QwenModelPath = Join-Path $solutionRoot $qwenDefinition.RelativePath }
    $QwenModelPath = [IO.Path]::GetFullPath($QwenModelPath)
    Test-DesktopQwenModel $QwenModelPath | Out-Null
} elseif ($QwenModelPath) { throw 'QwenModelPath requires IncludeQwenModel.' }
if ($releaseSettings.Update.Channel -ne $Channel -or $releaseSettings.Update.Platform -ne 'win-x64') {
    throw 'Desktop Update.Channel/Platform must match the release Channel and win-x64 platform for repair.'
}
# Remove RequireTransitionalSql only when the desktop composition no longer creates SQL services.
$resolvedFfmpegBundle = if ([string]::IsNullOrWhiteSpace($FfmpegBundlePath)) {
    [System.IO.Path]::GetFullPath((Join-Path $solutionRoot 'third_party\ffmpeg\win-x64'))
} else {
    [System.IO.Path]::GetFullPath($FfmpegBundlePath)
}

$ffmpegProfile = & (Join-Path $scriptRoot 'Test-FfmpegBundle.ps1') `
    -BundlePath $resolvedFfmpegBundle `
    -RequireReleaseApproval

$piperDefinition = Get-Content -LiteralPath (Join-Path $solutionRoot 'third_party/voice/PIPER_OFFLINE_APPROVED.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($ModelDeliveryMode -eq 'Bundled' -and -not $PiperBundlePath) {
    $PiperBundlePath = Join-Path $solutionRoot ('artifacts/piper-offline/' + $piperDefinition.bundleVersion)
}
if ($ModelDeliveryMode -eq 'Bundled') {
    $PiperBundlePath = [IO.Path]::GetFullPath($PiperBundlePath)
    & (Join-Path $scriptRoot 'Test-PiperOfflineBundle.ps1') -BundleDirectory $PiperBundlePath -ArtifactOnly | Out-Null
}
if (-not $OcrNativeRuntimePath) {
    $OcrNativeRuntimePath = Join-Path $solutionRoot 'artifacts/ocr-native-runtime/msvc-14.50.35719.0-win-x64'
}
$OcrNativeRuntimePath = [IO.Path]::GetFullPath($OcrNativeRuntimePath)
& (Join-Path $scriptRoot 'Test-OcrNativeRuntime.ps1') -BundleDirectory $OcrNativeRuntimePath | Out-Null

if (Test-Path -LiteralPath $releaseRoot) {
    $existingFiles = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File
    if ($existingFiles.Count -gt 0) {
        throw "Release output already contains files: $releaseRoot"
    }
}

New-Item -ItemType Directory -Force -Path $releaseRoot, $publishRoot, $packageRoot, $setupPublishRoot, $setupRoot | Out-Null

try {

$desktopPublishDirectoryArgument = "-p:PublishDir=$publishRoot\"
$bundlePiperPayload = ($ModelDeliveryMode -eq 'Bundled').ToString().ToLowerInvariant()
$modelPublishProperties = @("-p:RequirePiperOfflineBundle=$bundlePiperPayload", "-p:IncludePiperOfflinePayload=$bundlePiperPayload")
$modelPublishProperties += ('-p:StaticModelSetupRequired=' + ($ModelDeliveryMode -eq 'StaticArchive').ToString().ToLowerInvariant())
$modelPublishProperties += "-p:StaticModelReleaseChannel=$Channel"
$compiledIdentityArguments = @{ ExpectedDistributionProtocol = 2 }
if ($DeliveryMode -eq 'SharedVps') {
    $compiledIdentityArguments.ExpectedDistributionProtocol = 3
    $compiledIdentityArguments.ExpectedProfile = $ReleaseProfile
    $modelPublishProperties += '-p:DesktopDistributionProtocol=3'
    $modelPublishProperties += "-p:DesktopReleaseProfile=$ReleaseProfile"
    $modelPublishProperties += "-p:InformationalVersion=$Version+desktopbuild.$BuildNumber"
    $modelPublishProperties += '-p:IncludeSourceRevisionInInformationalVersion=false'
}
if ($PiperBundlePath) { $modelPublishProperties += "-p:PiperOfflineBundleDirectory=$PiperBundlePath" }
if ($resolvedModelTrustRoot) { $modelPublishProperties += "-p:ModelDistributionTrustRootPath=$resolvedModelTrustRoot" }

dotnet publish (Join-Path $solutionRoot 'TOOL-LOCAL\TOOL-LOCAL.csproj') `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:DesktopBuildNumber=$BuildNumber `
    -p:PublishSingleFile=true `
    -p:RequireMediaToolBundle=true `
    @modelPublishProperties `
    "-p:OcrNativeRuntimeDirectory=$OcrNativeRuntimePath" `
    ("-p:DesktopDeploymentSettingsPath={0}" -f $resolvedSettings) `
    ("-p:FfmpegBundleDirectory={0}" -f $resolvedFfmpegBundle) `
    $desktopPublishDirectoryArgument
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
if ($DeliveryMode -eq 'SharedVps') {
    $nativeVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishRoot 'TOOL-LOCAL.exe')).ProductVersion
    if ($nativeVersion -cne "$Version+desktopbuild.$BuildNumber") {
        throw 'The desktop native product version does not preserve its build identity.'
    }
}

if ($webView2Installer) {
    $prerequisiteRoot = Join-Path $publishRoot 'prerequisites'
    New-Item -ItemType Directory -Path $prerequisiteRoot -Force | Out-Null
    Copy-Item -LiteralPath $webView2Installer.Path -Destination (Join-Path $prerequisiteRoot 'webview2-runtime-x64.exe')
    Copy-Item -LiteralPath (Join-Path $solutionRoot 'third_party/webview2/PROVENANCE.md') -Destination (Join-Path $prerequisiteRoot 'WEBVIEW2-PROVENANCE.md')
    Copy-Item -LiteralPath (Join-Path $solutionRoot 'third_party/webview2/WEBVIEW2_INSTALLER_APPROVED.json') -Destination (Join-Path $prerequisiteRoot 'WEBVIEW2_INSTALLER_APPROVED.json')
    Test-ApprovedWebView2Installer -InstallerPath (Join-Path $prerequisiteRoot 'webview2-runtime-x64.exe') | Out-Null
}

# Sign only project-owned executables BEFORE probes, copies, inventory and ZIP hashes.
$desktopSigning = Invoke-DesktopAuthenticodeSigning -Root $publishRoot -Kind Desktop -Policy $authenticodePolicy

& (Join-Path $scriptRoot 'Test-DesktopSetupPublish.ps1') -PublishRoot $publishRoot -ProbeWebView2 `
    -ExpectedModelDeliveryMode $ModelDeliveryMode -ModelDistributionTrustRootPath $resolvedModelTrustRoot `
    -ExpectedVersion $Version -ExpectedBuildNumber $BuildNumber -ExpectedChannel $Channel @compiledIdentityArguments | Out-Null
if ($resolvedModelTrustRoot -and
    (Get-FileHash -LiteralPath $resolvedModelTrustRoot -Algorithm SHA256).Hash.ToLowerInvariant() -cne $modelPolicy.TrustRootSha256) {
    throw 'The approved model trust root changed during publishing.'
}

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

Copy-Item -LiteralPath $resolvedSettings -Destination (Join-Path $packageRoot 'appsettings.json') -Force
if ($DeliveryMode -eq 'SharedVps') {
    [ordered]@{
        schemaVersion = 1; distributionProtocol = 3; profile = $ReleaseProfile
        serverBaseUrl = $ServerBaseUrl.TrimEnd('/') + '/'; channel = $Channel; platform = 'win-x64'
        modelBundleId = 'models-20261016-03'; minimumBootstrapperVersion = $MinimumBootstrapperVersion
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packageRoot 'distribution-profile.json') -Encoding UTF8
}
if ($IncludeQwenModel) {
    Add-DesktopQwenModel -ModelPath $QwenModelPath -PackageRoot $packageRoot | Out-Null
}
& (Join-Path $scriptRoot 'Test-DesktopDeploymentSettings.ps1') `
    -SettingsPath (Join-Path $packageRoot 'appsettings.json') -ExpectedServerBaseUrl $ServerBaseUrl `
    -AllowTransitionalSql:$AllowTransitionalSql -RequireTransitionalSql | Out-Null
Test-DesktopAuthenticodeSignatures -Root $packageRoot -Kind Desktop -Policy $authenticodePolicy | Out-Null

$managedFiles = Get-ChildItem -LiteralPath $packageRoot -Recurse -File | ForEach-Object {
    $_.FullName.Substring($packageRoot.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
} | Sort-Object
$managedFiles += 'update-manifest.json'
$managedFiles += 'package-inventory.json'
$requiredMediaFiles = @(
    'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'vcomp140.dll',
    'third_party/ocr/MSVC_RUNTIME.json', 'third_party/ocr/MSVC-NOTICE.md',
    'tools/ffmpeg/ffmpeg.exe',
    'tools/ffmpeg/ffprobe.exe',
    'tools/ffmpeg/LICENSE.txt',
    'tools/ffmpeg/PROVENANCE.md',
    'tools/ffmpeg/checksums.sha256',
    'workers/piper_worker.py', 'workers/piper-requirements.lock',
    'third_party/voice/PIPER_OFFLINE_APPROVED.json'
)
if ($ModelDeliveryMode -eq 'Bundled') {
    $requiredMediaFiles += @(
        ('components/piper/' + $piperDefinition.bundleVersion + '/manifest.json'),
        ('components/piper/' + $piperDefinition.bundleVersion + '/piper-offline.zip')
    )
}
if ($IncludeQwenModel) { $requiredMediaFiles += $qwenDefinition.RelativePath }
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
New-DesktopPackageInventory -PackageRoot $packageRoot

$packagePath = Join-Path $releaseRoot ("VideoMaker-{0}-{1}-win-x64.zip" -f $Version, $BuildNumber)
New-DesktopPackageZip -PackageRoot $packageRoot -DestinationPath $packagePath
$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$packageSize = (Get-Item -LiteralPath $packagePath).Length
$uncompressedPackageBytes = [long](Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Measure-Object Length -Sum).Sum
$serverUploadCompatible = $packageSize -le 2147483648 -and $uncompressedPackageBytes -le 2147483648
if ($DeliveryMode -in @('ExternalVps', 'SharedVps')) {
    $downloadPolicy = Get-DesktopExternalDownloadPolicy
    if ($packageSize -gt $downloadPolicy.MaximumArchiveBytes -or $uncompressedPackageBytes -gt $downloadPolicy.MaximumExpandedBytes) {
        throw 'External application package exceeds the compiled archive/expanded size policy. Model archives use their separate policy.'
    }
}
if ($DeliveryMode -eq 'Updater' -and -not $serverUploadCompatible) {
    throw 'Package or expanded content exceeds the default server artifact limit (2 GiB). Use Direct delivery or review server limits separately.'
}

# Validate what recipients actually extract, including OCR native dependencies and fixture inference.
$extractedRoot = Join-Path $workRoot 'extracted-zip'
$extraction = Expand-DesktopPackageZip -ZipPath $packagePath -DestinationRoot $extractedRoot
Test-DesktopPackageInventory -PackageRoot $extractedRoot | Out-Null
Test-DesktopAuthenticodeSignatures -Root $extractedRoot -Kind Desktop -Policy $authenticodePolicy | Out-Null
& (Join-Path $scriptRoot 'Test-DesktopSetupPublish.ps1') -PublishRoot $extractedRoot -ProbeWebView2 -ProbeBundledComponents `
    -ProbePiperOffline:($ModelDeliveryMode -eq 'Bundled') -RequireQwenModel:$IncludeQwenModel -ProbeQwenOffline:$IncludeQwenModel `
    -AcceptQwenResourceWarning:$AcceptQwenResourceWarning -ExpectedModelDeliveryMode $ModelDeliveryMode `
    -ModelDistributionTrustRootPath $resolvedModelTrustRoot `
    -ExpectedVersion $Version -ExpectedBuildNumber $BuildNumber -ExpectedChannel $Channel @compiledIdentityArguments | Out-Null
if ($resolvedModelTrustRoot -and
    (Get-FileHash -LiteralPath $resolvedModelTrustRoot -Algorithm SHA256).Hash.ToLowerInvariant() -cne $modelPolicy.TrustRootSha256) {
    throw 'The approved model trust root changed during package verification.'
}
('{0}  {1}' -f $packageHash, [IO.Path]::GetFileName($packagePath)) | Set-Content -LiteralPath (Join-Path $releaseRoot 'checksums.sha256') -Encoding ASCII

if ($DeliveryMode -eq 'SharedVps') {
    # The shared Setup is built independently. A new application ZIP must never
    # rebuild or rewrite its executable, nor demand a registration JSON from Admin.
    [ordered]@{
        schemaVersion = 1; deliveryMode = 'SharedVps'; version = $Version; buildNumber = $BuildNumber
        package = [IO.Path]::GetFileName($packagePath); sizeBytes = $packageSize; sha256 = $packageHash
        profile = $ReleaseProfile; minimumBootstrapperVersion = $MinimumBootstrapperVersion
        setupBuilt = $false; productionQualified = $false
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot 'package-report.json') -Encoding UTF8
    [pscustomobject]@{ Version = $Version; BuildNumber = $BuildNumber; Channel = $Channel
        Package = $packagePath; SizeBytes = $packageSize; Sha256 = $packageHash
        DeliveryMode = $DeliveryMode; Setup = $null; SetupBuilt = $false; Registration = $null }
    return
}

$setupModelProperties = @('-p:SetupModelDeliveryMode=' + $ModelDeliveryMode)
if ($ModelDeliveryMode -eq 'StaticArchive') {
    $setupModelProperties += @("-p:SetupDesktopVersion=$Version", "-p:SetupDesktopBuildNumber=$BuildNumber", "-p:SetupDesktopSha256=$packageHash",
        "-p:SetupWebView2InstallerSizeBytes=$($webView2Installer.SizeBytes)", "-p:SetupWebView2InstallerSha256=$($webView2Installer.Sha256)",
        "-p:SetupWebView2InstallerPublisherThumbprint=$($webView2Installer.PublisherThumbprint)")
}
dotnet publish (Join-Path $solutionRoot 'TOOL-SETUP\TOOL-SETUP.csproj') `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:SetupServerBaseUrl=$ServerBaseUrl `
    -p:SetupChannel=$Channel `
    -p:SetupPlatform=win-x64 `
    @setupModelProperties `
    ("-p:PublishDir={0}\" -f $setupPublishRoot)
if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }

$setupExecutable = Join-Path $setupPublishRoot 'VideoMaker Setup.exe'
if (-not (Test-Path -LiteralPath $setupExecutable -PathType Leaf)) { throw 'Setup executable was not published.' }
$setupSigning = Invoke-DesktopAuthenticodeSigning -Root $setupPublishRoot -Kind Setup -Policy $authenticodePolicy
Copy-Item -LiteralPath $setupExecutable -Destination (Join-Path $setupRoot 'VideoMaker Setup.exe') -Force
Test-DesktopAuthenticodeSignatures -Root $setupRoot -Kind Setup -Policy $authenticodePolicy | Out-Null
$setupHash = (Get-FileHash -LiteralPath (Join-Path $setupRoot 'VideoMaker Setup.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$setupSize = (Get-Item -LiteralPath (Join-Path $setupRoot 'VideoMaker Setup.exe')).Length
$signingReport = [ordered]@{
    SchemaVersion = 1; Mode = $authenticodePolicy.Mode; Required = [bool]$RequireAuthenticode
    Signed = [bool]$desktopSigning.Signed -and [bool]$setupSigning.Signed
    CertificateThumbprint = $authenticodePolicy.CertificateThumbprint
    Desktop = $desktopSigning; Setup = $setupSigning
    PackageSha256 = $packageHash; SetupSha256 = $setupHash; SetupSizeBytes = $setupSize
    ProductionQualified = $false
}
$signingReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $releaseRoot 'authenticode-report.json') -Encoding UTF8
Add-Content -LiteralPath (Join-Path $releaseRoot 'checksums.sha256') -Value ('{0}  setup/VideoMaker Setup.exe' -f $setupHash) -Encoding ASCII

$registrationPath = $null
if ($DeliveryMode -eq 'ExternalVps') {
    # Last: inspect the actual fresh Setup, re-read/hash the final ZIP, then emit a separate request.
    # No network, upload, server registration or production-readiness claim is performed here.
    $setupBinding = Invoke-DesktopSetupBindingProbe -SetupPath (Join-Path $setupRoot 'VideoMaker Setup.exe')
    $registrationPath = Join-Path $releaseRoot 'external-registration.json'
    $registration = New-DesktopExternalRegistration -PackagePath $packagePath -SetupPath (Join-Path $setupRoot 'VideoMaker Setup.exe') `
        -Version $Version -BuildNumber $BuildNumber -Channel $Channel -ServerBaseUrl $ServerBaseUrl `
        -DownloadUrl $PackageDownloadUrl -SetupBinding $setupBinding -OutputPath $registrationPath `
        -ExpectedPackageSha256 $packageHash -ExpectedSetupSha256 $setupHash
    $registrationHash = (Get-FileHash -LiteralPath $registrationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Add-Content -LiteralPath (Join-Path $releaseRoot 'checksums.sha256') -Value ('{0}  external-registration.json' -f $registrationHash) -Encoding ASCII
}

[pscustomobject]@{
    Version = $Version
    BuildNumber = $BuildNumber
    Channel = $Channel
    Package = $packagePath
    SizeBytes = (Get-Item -LiteralPath $packagePath).Length
    Sha256 = $packageHash
    Setup = (Join-Path $setupRoot 'VideoMaker Setup.exe')
    SetupSha256 = $setupHash
    SetupSizeBytes = $setupSize
    AuthenticodeMode = $authenticodePolicy.Mode
    AuthenticodeSigned = $signingReport.Signed
    AuthenticodeProductionQualified = $false
    FfmpegVersion = $publishedFfmpegProfile.Version
    QwenModelIncluded = [bool]$IncludeQwenModel
    ModelDeliveryMode = $ModelDeliveryMode
    ModelTrustRepository = $modelPolicy.RepositoryId
    DeliveryMode = $DeliveryMode
    PackageDownloadUrl = $PackageDownloadUrl
    ExternalRegistration = $registrationPath
    UploadedOrPublished = $false
    ServerUploadCompatible = $serverUploadCompatible
    ExtractedBytes = $extraction.ExtractedBytes
} | Format-List
}
finally {
    $expectedWorkParent = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) 'VideoMakerRelease')).TrimEnd('\') + '\'
    $resolvedWorkRoot = [System.IO.Path]::GetFullPath($workRoot)
    if (-not $resolvedWorkRoot.StartsWith($expectedWorkParent, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedWorkRoot.TrimEnd('\') -eq $expectedWorkParent.TrimEnd('\')) {
        throw 'Refusing cleanup outside the isolated release workspace.'
    }
    if (Test-Path -LiteralPath $resolvedWorkRoot -PathType Container) {
        Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force
    }
}
