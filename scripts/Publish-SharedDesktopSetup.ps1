param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$BootstrapperVersion,
    [Parameter(Mandatory = $true)][string]$ServerBaseUrl,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$WebView2InstallerPath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$')][string]$Profile = 'Production',
    [ValidateSet('Stable', 'Beta', 'Development')][string]$Channel = 'Stable',
    [ValidateSet('None', 'WindowsStore')][string]$AuthenticodeMode = 'None',
    [string]$SigningCertificateThumbprint = '',
    [ValidateSet('CurrentUser', 'LocalMachine')][string]$SigningCertificateStoreLocation = 'CurrentUser',
    [string]$SignToolPath = '', [string]$SigningTimestampUrl = '', [switch]$RequireAuthenticode,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
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
Assert-SharedDesktopPublishInput -Channel $Channel -Versions @($BootstrapperVersion)
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
Import-Module (Join-Path $scriptRoot 'DesktopExternalRelease.psm1') -Force
Import-Module (Join-Path $scriptRoot 'DesktopAuthenticode.psm1') -Force
Import-Module (Join-Path $scriptRoot 'WebView2Prerequisite.psm1') -Force
$api = Test-DesktopCustomerApiUrl $ServerBaseUrl
$binding = [ordered]@{ schemaVersion = 1; version = $BootstrapperVersion; apiBaseUrl = $api
    profile = $Profile; channel = $Channel; platform = 'win-x64'; distributionProtocol = 3; modelBundleId = 'models-20261016-03' }
if ([Version]$BootstrapperVersion -ge [Version]'1.3.0') {
    $binding['supportedCatalogProtocols'] = '3,4,5'
} elseif ([Version]$BootstrapperVersion -ge [Version]'1.2.0') {
    $binding['supportedCatalogProtocols'] = '3,4'
}
$bindingJson = $binding | ConvertTo-Json -Compress
$bindingMetadata = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($bindingJson))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Shared Setup output must be a new directory; published executables are immutable.' }
$webView = Test-ApprovedWebView2Installer -InstallerPath $WebView2InstallerPath
$signingPolicy = New-DesktopAuthenticodePolicy -Mode $AuthenticodeMode `
    -CertificateThumbprint $SigningCertificateThumbprint -CertificateStoreLocation $SigningCertificateStoreLocation `
    -SignToolPath $SignToolPath -TimestampUrl $SigningTimestampUrl -RequireAuthenticode:$RequireAuthenticode
$workRoot = Join-Path ([IO.Path]::GetTempPath()) ('VideoMakerSharedSetup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workRoot | Out-Null
try {
    dotnet publish (Join-Path $solutionRoot 'TOOL-SETUP/TOOL-SETUP.csproj') -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:SetupMode=SharedBootstrapper -p:SetupModelDeliveryMode=StaticArchive `
        "-p:SetupServerBaseUrl=$api" "-p:SetupProfile=$Profile" "-p:SetupChannel=$Channel" -p:SetupPlatform=win-x64 `
        "-p:SetupBootstrapperVersion=$BootstrapperVersion" '-p:SetupDesktopVersion=' '-p:SetupDesktopBuildNumber=0' '-p:SetupDesktopSha256=' `
        "-p:SetupBindingMetadata=$bindingMetadata" `
        "-p:SetupWebView2InstallerPath=$($webView.Path)" `
        "-p:SetupWebView2InstallerSizeBytes=$($webView.SizeBytes)" "-p:SetupWebView2InstallerSha256=$($webView.Sha256)" `
        "-p:SetupWebView2InstallerPublisherThumbprint=$($webView.PublisherThumbprint)" "-p:PublishDir=$workRoot\"
    if ($LASTEXITCODE -ne 0) { throw 'Shared Setup publish failed.' }
    $null = Invoke-DesktopAuthenticodeSigning -Root $workRoot -Kind Setup -Policy $signingPolicy
    Test-DesktopAuthenticodeSignatures -Root $workRoot -Kind Setup -Policy $signingPolicy | Out-Null
    $executable = Join-Path $workRoot 'VideoMaker Setup.exe'
    $nativeBinding = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).Comments
    if ($nativeBinding -cne ('VideoMaker.SharedBootstrapper.v1:' + $bindingMetadata)) {
        throw 'The published Setup native version resource does not preserve its compiled shared binding.'
    }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $executable; $start.Arguments = '--inspect-shared-bootstrapper'
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw 'Cannot inspect shared Setup.' }
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); $process.WaitForExit(); throw 'Shared Setup inspection timed out.' }
        $null = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'Shared Setup rejected its compiled profile.' }
        $metadata = $stdout.GetAwaiter().GetResult() | ConvertFrom-Json
    } finally { $process.Dispose() }
    if ($metadata.schemaVersion -ne 1 -or
        $metadata.version -cne $BootstrapperVersion -or $metadata.profile -cne $Profile -or
        $metadata.apiBaseUrl.TrimEnd('/') -cne $api.TrimEnd('/') -or $metadata.channel -cne $Channel -or
        $metadata.platform -cne 'win-x64' -or $metadata.distributionProtocol -ne 3 -or
        $metadata.modelBundleId -cne 'models-20261016-03' -or
        ([Version]$BootstrapperVersion -ge [Version]'1.2.0' -and $metadata.supportedCatalogProtocols -cne $binding.supportedCatalogProtocols)) { throw 'Compiled shared Setup metadata differs from the requested profile.' }
    New-Item -ItemType Directory -Path $output | Out-Null
    $hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    $fileName = if ([Version]$BootstrapperVersion -ge [Version]'1.3.0') { 'Setup-{0}-win-x64-{1}.exe' -f $BootstrapperVersion, $hash } else { 'VideoMaker Setup.exe' }
    $destination = Join-Path $output $fileName
    Copy-Item -LiteralPath $executable -Destination $destination
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) { throw 'Copied Setup hash changed.' }
    $size = (Get-Item -LiteralPath $destination).Length
    [ordered]@{
        binding = $binding; activate = $false
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'shared-bootstrapper.json') -Encoding UTF8
    [ordered]@{ schemaVersion = 1; fileName = $fileName; sizeBytes = $size; sha256 = $hash
        binding = $binding; nativeResourceVerified = $true; embeddedWebView2Installer = $true; productionQualified = $false
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'bootstrapper-report.json') -Encoding UTF8
    ('{0}  {1}' -f $hash, $fileName) | Set-Content -LiteralPath (Join-Path $output 'checksums.sha256') -Encoding ASCII
    [pscustomobject]@{ Setup = $destination; Metadata = (Join-Path $output 'shared-bootstrapper.json'); Sha256 = $hash; SizeBytes = $size }
} finally {
    $ownedRoot = [IO.Path]::GetFullPath($workRoot)
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($ownedRoot.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($ownedRoot) -match '^VideoMakerSharedSetup-[0-9a-f]{32}$' -and (Test-Path -LiteralPath $ownedRoot)) {
        Remove-Item -LiteralPath $ownedRoot -Recurse -Force
    }
}
