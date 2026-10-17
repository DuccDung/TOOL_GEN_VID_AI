#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SettingsPath,
    [ValidateSet('Bundled', 'Server', 'StaticArchive')][string]$ModelDeliveryMode = 'Bundled',
    [string]$TrustRootPath = '',
    [switch]$IncludeQwenModel,
    [string]$QwenModelPath = '',
    [string]$PiperBundlePath = '',
    [string]$PublishRoot = '',
    [switch]$ProbeEmbeddedTrustRoot
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ModelDistributionHandover.psm1') -Force
$settings = Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$distribution = $settings.ModelDistribution
$enabled = $null -ne $distribution -and $distribution.Enabled -is [bool] -and $distribution.Enabled
$serverMode = $ModelDeliveryMode -eq 'Server'
$staticMode = $ModelDeliveryMode -eq 'StaticArchive'
if ($staticMode) {
    if ($null -eq $distribution -or $distribution.Mode -cne 'StaticArchive' -or $enabled -or
        $TrustRootPath -or $IncludeQwenModel -or $QwenModelPath -or $PiperBundlePath -or
        -not [string]::IsNullOrWhiteSpace([string]$distribution.BaseUrl) -or
        -not [string]::IsNullOrWhiteSpace([string]$distribution.CacheRoot)) {
        throw 'StaticArchive requires its explicit mode, compiled model pins and per-user roots; signed catalog, URL/cache overrides and bundled model arguments are not allowed.'
    }
    foreach ($feature in @('VietsubEnabled', 'VietsubOcrEnabled', 'VietsubLocalTranslationEnabled', 'VietsubLocalVoiceEnabled')) {
        if ($settings.Features.$feature -isnot [bool] -or -not $settings.Features.$feature) {
            throw 'A StaticArchive release must enable all four required local components.'
        }
    }
}
if (-not $staticMode -and $null -ne $distribution -and $distribution.Mode -eq 'StaticArchive') {
    throw 'StaticArchive settings require a StaticArchive release and its Setup binding.'
}
if ($serverMode -and (-not $enabled -or $IncludeQwenModel -or $QwenModelPath -or $PiperBundlePath)) {
    throw 'Server model delivery requires ModelDistribution.Enabled=true and must not include local model payload arguments.'
}
if (-not $serverMode -and -not $staticMode -and $settings.Features.VietsubLocalTranslationEnabled -and -not $IncludeQwenModel) {
    throw 'An enabled local translation Bundled release must include the approved offline Qwen model.'
}
$trustStamp = $null
$repositoryId = ''
$publicKeyCount = 0
if ($enabled -or $serverMode -or $TrustRootPath) {
    if ($enabled -or $serverMode) {
        $origin = $null
        if (-not [Uri]::TryCreate([string]$distribution.BaseUrl, [UriKind]::Absolute, [ref]$origin) -or
            $origin.Scheme -cne 'https' -or $origin.Port -ne 443 -or $origin.UserInfo -or $origin.Query -or $origin.Fragment -or $origin.IsLoopback) {
            throw 'ModelDistribution.BaseUrl must be an explicit public HTTPS/443 origin without credentials, query or fragment.'
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$distribution.CacheRoot)) {
            throw 'The release model cache must use its per-user default; do not package private cache paths.'
        }
    }
    if (-not $TrustRootPath) { throw 'Enabled model distribution requires an explicit approved public trust root.' }
    $trustPath = Assert-ModelSafePath ([IO.Path]::GetFullPath($TrustRootPath))
    $trustStamp = Get-ModelFileStamp $trustPath
    if ($trustStamp.sizeBytes -lt 1 -or $trustStamp.sizeBytes -gt 65536) { throw 'Invalid model distribution trust-root size.' }
    $text = [IO.File]::ReadAllText($trustPath, [Text.Encoding]::UTF8)
    if ($text -match '(?i)PRIVATE[ _-]?KEY|BEGIN (?:EC |RSA |ENCRYPTED )?PRIVATE') { throw 'A private key must never be embedded in a desktop release.' }
    $trust = $text | ConvertFrom-Json
    $properties = @($trust.PSObject.Properties.Name | Sort-Object)
    if (($properties -join ',') -cne 'publicKeys,repositoryId,schemaVersion' -or $trust.schemaVersion -ne 1 -or
        $trust.repositoryId -cnotmatch '^[A-Za-z0-9_.-]{1,100}$' -or $trust.publicKeys -isnot [pscustomobject]) {
        throw 'Invalid public model distribution trust-root schema.'
    }
    $keys = @($trust.publicKeys.PSObject.Properties)
    if ($keys.Count -lt 1 -or $keys.Count -gt 16) { throw 'The model distribution trust root requires 1 to 16 public keys.' }
    # DER SubjectPublicKeyInfo: id-ecPublicKey + prime256v1, uncompressed point.
    # Import into Windows CNG as well so a malformed/off-curve point is rejected.
    $prefix = [byte[]]@(0x30,0x59,0x30,0x13,0x06,0x07,0x2a,0x86,0x48,0xce,0x3d,0x02,0x01,0x06,0x08,0x2a,0x86,0x48,0xce,0x3d,0x03,0x01,0x07,0x03,0x42,0x00,0x04)
    foreach ($entry in $keys) {
        if ($entry.Name -cnotmatch '^[A-Za-z0-9_.-]{1,100}$' -or $entry.Value -isnot [string] -or $entry.Value.Length -gt 4096 -or
            $entry.Value -cnotmatch '\A-----BEGIN PUBLIC KEY-----\r?\n(?<body>[A-Za-z0-9+/=\r\n]+)\r?\n-----END PUBLIC KEY-----(?:\r?\n)?\z') {
            throw 'The model trust root may contain only PEM public P-256 keys.'
        }
        try { $der = [Convert]::FromBase64String($Matches.body) } catch { throw 'Invalid model public key encoding.' }
        if ($der.Length -ne 91) { throw 'The model public key must use ECDSA P-256.' }
        for ($index = 0; $index -lt $prefix.Length; $index++) {
            if ($der[$index] -ne $prefix[$index]) { throw 'The model public key must use ECDSA prime256v1.' }
        }
        $blob = New-Object byte[] 72
        [Array]::Copy([byte[]]@(0x45,0x43,0x53,0x31,0x20,0,0,0), 0, $blob, 0, 8)
        [Array]::Copy($der, 27, $blob, 8, 64)
        $cng = $null
        $ec = $null
        try {
            $cng = [Security.Cryptography.CngKey]::Import($blob, [Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
            $ec = New-Object Security.Cryptography.ECDsaCng($cng)
            if ($ec.KeySize -ne 256) { throw 'Invalid model public key size.' }
        } catch { throw 'The model public key is not a valid ECDSA P-256 public point.' }
        finally { if ($ec) { $ec.Dispose() }; if ($cng) { $cng.Dispose() } }
    }
    $repositoryId = $trust.repositoryId
    $publicKeyCount = $keys.Count
}

if ($PublishRoot) {
    $root = Assert-ModelSafePath ([IO.Path]::GetFullPath($PublishRoot))
    if ($serverMode -or $staticMode) {
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
            if ($file.Extension -ieq '.gguf' -or $file.Name -ieq 'piper-offline.zip' -or
                $file.FullName -match '[\\/]components[\\/]piper[\\/]') {
                throw 'A remote model-delivery package must not contain bundled Qwen or Piper payloads.'
            }
        }
    }
    $approved = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../third_party/voice/PIPER_OFFLINE_APPROVED.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($entry in @(@('workers/piper_worker.py', $approved.workerSha256), @('workers/piper-requirements.lock', $approved.requirementsSha256))) {
        $stamp = Get-ModelFileStamp (Join-Path $root $entry[0])
        if ($stamp.sha256 -cne $entry[1]) { throw 'Published Piper worker/requirements do not match the approved bundle.' }
    }
    $definition = Join-Path $root 'third_party/voice/PIPER_OFFLINE_APPROVED.json'
    $definitionStamp = Get-ModelFileStamp $definition
    $sourceStamp = Get-ModelFileStamp (Join-Path $PSScriptRoot '../third_party/voice/PIPER_OFFLINE_APPROVED.json')
    if ($definitionStamp.sha256 -cne $sourceStamp.sha256) { throw 'The published Piper approved definition differs from source.' }
    if ($staticMode) {
        Import-Module (Join-Path $PSScriptRoot 'WebView2Prerequisite.psm1') -Force
        Test-ApprovedWebView2Installer -InstallerPath (Join-Path $root 'prerequisites/webview2-runtime-x64.exe') | Out-Null
    }
    if ($ProbeEmbeddedTrustRoot) {
        if (-not $enabled -or -not $trustStamp) { throw 'Embedded model trust probing requires enabled distribution and a public trust root.' }
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = Join-Path $root 'TOOL-LOCAL.exe'
        $start.Arguments = '--check-model-distribution-config'
        $start.WorkingDirectory = [IO.Path]::GetTempPath()
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = New-Object Diagnostics.Process
        $process.StartInfo = $start
        try {
            if (-not $process.Start()) { throw 'Cannot probe the embedded model trust root.' }
            $output = $process.StandardOutput.ReadToEndAsync()
            $errorOutput = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(30000)) { $process.Kill(); $process.WaitForExit(); throw 'Embedded model trust probe timed out.' }
            $null = $errorOutput.GetAwaiter().GetResult()
            try { $report = $output.GetAwaiter().GetResult() | ConvertFrom-Json } catch { throw 'Invalid embedded model trust probe report.' }
            if ($process.ExitCode -ne 0 -or $report.ModelDistributionConfigured -ne $true -or
                $report.RepositoryId -cne $repositoryId -or $report.PublicKeyCount -ne $publicKeyCount -or
                $report.TrustRootSha256 -cne $trustStamp.sha256) { throw 'Published model trust root differs from the approved build input.' }
        } finally { $process.Dispose() }
    }
}
[pscustomobject]@{ ModelDeliveryMode = $ModelDeliveryMode; DistributionEnabled = [bool]$enabled; RepositoryId = $repositoryId; PublicKeyCount = $publicKeyCount; TrustRootSha256 = $(if ($trustStamp) { $trustStamp.sha256 } else { '' }); PayloadsBundled = -not ($serverMode -or $staticMode) }
