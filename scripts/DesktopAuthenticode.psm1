#requires -Version 5.1
Set-StrictMode -Version 2

function Assert-DesktopSigningPath([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path) -or $Path -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+[\\/])') {
        throw 'Authenticode paths must be absolute.'
    }
    $full = [IO.Path]::GetFullPath($Path)
    $item = $full
    while ($item) {
        if (Test-Path -LiteralPath $item) {
            if ((Get-Item -LiteralPath $item -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Authenticode paths must not contain reparse points.'
            }
        }
        $parent = [IO.Path]::GetDirectoryName($item.TrimEnd('\', '/'))
        if (-not $parent -or $parent -eq $item) { break }
        $item = $parent
    }
    return $full
}

function Get-DesktopSigningCertificate([string]$Thumbprint, [string]$StoreLocation) {
    $certificate = Get-Item -LiteralPath ('Cert:\' + $StoreLocation + '\My\' + $Thumbprint) -ErrorAction Stop
    $codeSigning = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
        ForEach-Object { $_.EnhancedKeyUsages } | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }).Count -gt 0
    [pscustomobject]@{
        Thumbprint = $certificate.Thumbprint
        HasPrivateKey = $certificate.HasPrivateKey
        NotBeforeUtc = $certificate.NotBefore.ToUniversalTime()
        NotAfterUtc = $certificate.NotAfter.ToUniversalTime()
        CodeSigningUsage = $codeSigning
    }
}

function New-DesktopAuthenticodePolicy {
    [CmdletBinding()]
    param(
        [ValidateSet('None', 'WindowsStore')][string]$Mode = 'None',
        [string]$CertificateThumbprint = '',
        [ValidateSet('CurrentUser', 'LocalMachine')][string]$CertificateStoreLocation = 'CurrentUser',
        [string]$SignToolPath = '',
        [string]$TimestampUrl = '',
        [switch]$RequireAuthenticode
    )
    if ($Mode -eq 'None') {
        if ($RequireAuthenticode -or $CertificateThumbprint -or $SignToolPath -or $TimestampUrl -or $CertificateStoreLocation -ne 'CurrentUser') {
            throw 'Authenticode is required or configured; select WindowsStore explicitly.'
        }
        return [pscustomobject]@{ Enabled = $false; Mode = 'None'; CertificateThumbprint = ''; CertificateStoreLocation = ''; SignToolPath = ''; TimestampUrl = '' }
    }
    $thumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    if ($thumbprint -cnotmatch '^[0-9A-F]{40}$') { throw 'Authenticode requires an explicit certificate thumbprint.' }
    $timestamp = $null
    if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestamp) -or
        $timestamp.Scheme -cne 'https' -or $timestamp.Port -ne 443 -or $timestamp.UserInfo -or
        $timestamp.Query -or $timestamp.Fragment -or $timestamp.IsLoopback) {
        throw 'Authenticode requires an explicit HTTPS/443 RFC3161 timestamp endpoint without credentials or query.'
    }
    $tool = Assert-DesktopSigningPath $SignToolPath
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf) -or [IO.Path]::GetFileName($tool) -ine 'signtool.exe') {
        throw 'Authenticode requires an explicit Windows SDK signtool.exe path.'
    }
    try { $certificate = Get-DesktopSigningCertificate $thumbprint $CertificateStoreLocation }
    catch { throw 'The selected Authenticode certificate cannot be read from the configured Windows store.' }
    $now = [DateTime]::UtcNow
    if ($certificate.Thumbprint -ine $thumbprint -or -not $certificate.HasPrivateKey -or -not $certificate.CodeSigningUsage -or
        $certificate.NotBeforeUtc -gt $now -or $certificate.NotAfterUtc -le $now) {
        throw 'The selected Authenticode certificate must be current, match its thumbprint, have a private key and allow code signing.'
    }
    [pscustomobject]@{
        Enabled = $true; Mode = 'WindowsStore'; CertificateThumbprint = $thumbprint
        CertificateStoreLocation = $CertificateStoreLocation; SignToolPath = $tool; TimestampUrl = $timestamp.AbsoluteUri
    }
}

function Get-DesktopSigningTargets([string]$Root, [string]$Kind) {
    $fullRoot = (Assert-DesktopSigningPath $Root).TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath $fullRoot -PathType Container)) { throw 'Authenticode publish root is missing.' }
    $relativePaths = if ($Kind -eq 'Desktop') {
        @('TOOL-LOCAL.exe', 'VideoMaker.Updater.exe', '_translation_worker/VideoMaker.Vietsub.TranslationWorker.exe')
    } else { @('VideoMaker Setup.exe') }
    foreach ($relative in $relativePaths) {
        $path = Assert-DesktopSigningPath (Join-Path $fullRoot $relative)
        if (-not $path.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'A required project executable is missing from the Authenticode publish root.' }
        [pscustomobject]@{ RelativePath = $relative; Path = $path }
    }
}

function Get-DesktopAuthenticodeSignature([string]$Path) {
    Get-AuthenticodeSignature -LiteralPath $Path -ErrorAction Stop
}

function Invoke-DesktopSignTool($Policy, [string[]]$Arguments) {
    # Arguments contain no PFX password, private key or credential. Suppress tool output nevertheless.
    & $Policy.SignToolPath @Arguments 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Windows SDK SignTool failed; no release artifact may be finalized.' }
}

function Assert-DesktopAuthenticodeSignature($Signature, $Policy) {
    if ([string]$Signature.Status -ne 'Valid' -or $null -eq $Signature.SignerCertificate -or
        $Signature.SignerCertificate.Thumbprint -ine $Policy.CertificateThumbprint -or
        $null -eq $Signature.TimeStamperCertificate) {
        throw 'The project executable must have a valid embedded signature from the selected certificate and a timestamp.'
    }
}

function Test-DesktopAuthenticodeSignatures {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][ValidateSet('Desktop', 'Setup')][string]$Kind,
        [Parameter(Mandatory = $true)]$Policy)
    if (-not $Policy.Enabled) { return [pscustomobject]@{ Signed = $false; Mode = 'None'; CertificateThumbprint = ''; Timestamped = $false; Files = @() } }
    $targets = @(Get-DesktopSigningTargets $Root $Kind)
    $verified = @()
    foreach ($target in $targets) {
        $signature = Get-DesktopAuthenticodeSignature $target.Path
        Assert-DesktopAuthenticodeSignature $signature $Policy
        Invoke-DesktopSignTool $Policy @('verify', '/pa', '/all', '/tw', $target.Path)
        $verified += [pscustomobject]@{
            Path = $target.RelativePath; SizeBytes = (Get-Item -LiteralPath $target.Path).Length
            Sha256 = (Get-FileHash -LiteralPath $target.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    [pscustomobject]@{ Signed = $true; Mode = $Policy.Mode; CertificateThumbprint = $Policy.CertificateThumbprint; Timestamped = $true; Files = $verified }
}

function Invoke-DesktopAuthenticodeSigning {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][ValidateSet('Desktop', 'Setup')][string]$Kind,
        [Parameter(Mandatory = $true)]$Policy)
    if (-not $Policy.Enabled) { return Test-DesktopAuthenticodeSignatures -Root $Root -Kind $Kind -Policy $Policy }
    # Resolve ALL required files first; never partially sign a publish with a missing project executable.
    $targets = @(Get-DesktopSigningTargets $Root $Kind)
    foreach ($target in $targets) {
        $before = Get-DesktopAuthenticodeSignature $target.Path
        if ([string]$before.Status -eq 'NotSigned') {
            $arguments = @('sign', '/sha1', $Policy.CertificateThumbprint, '/s', 'My', '/fd', 'SHA256',
                '/tr', $Policy.TimestampUrl, '/td', 'SHA256')
            if ($Policy.CertificateStoreLocation -eq 'LocalMachine') { $arguments += '/sm' }
            $arguments += $target.Path
            Invoke-DesktopSignTool $Policy $arguments
        } else {
            # Preserve a known signature; never replace an unrelated signer or hide a broken signature.
            Assert-DesktopAuthenticodeSignature $before $Policy
        }
    }
    Test-DesktopAuthenticodeSignatures -Root $Root -Kind $Kind -Policy $Policy
}

Export-ModuleMember -Function New-DesktopAuthenticodePolicy, Invoke-DesktopAuthenticodeSigning, Test-DesktopAuthenticodeSignatures
