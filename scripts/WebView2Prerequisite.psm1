#requires -Version 5.1
Set-StrictMode -Version Latest

function Get-ApprovedWebView2Installer {
    $path = Join-Path $PSScriptRoot '../third_party/webview2/WEBVIEW2_INSTALLER_APPROVED.json'
    $pin = [IO.File]::ReadAllText([IO.Path]::GetFullPath($path)) | ConvertFrom-Json
    if ($pin.schemaVersion -ne 1 -or $pin.platform -cne 'win-x64' -or
        $pin.packageRelativePath -cne 'prerequisites/webview2-runtime-x64.exe' -or
        $pin.sizeBytes -le 0 -or $pin.sizeBytes -gt 536870912 -or
        $pin.sha256 -cnotmatch '^[a-f0-9]{64}$' -or
        $pin.publisherThumbprint -cnotmatch '^[A-F0-9]{40}$' -or
        $pin.signatureVerificationRequired -ne $true) {
        throw 'Invalid approved WebView2 installer definition.'
    }
    return $pin
}

function Test-ApprovedWebView2Installer {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$InstallerPath)
    $pin = Get-ApprovedWebView2Installer
    $full = [IO.Path]::GetFullPath($InstallerPath)
    $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    while ($null -ne $item) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'WebView2 installer must not use a reparse point.'
        }
        if ($item -is [IO.FileInfo]) { $item = $item.Directory } else { $item = $item.Parent }
    }
    $file = Get-Item -LiteralPath $full -ErrorAction Stop
    if ($file -isnot [IO.FileInfo] -or $file.Length -ne $pin.sizeBytes -or
        (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pin.sha256) {
        throw 'WebView2 installer does not match the approved size and SHA-256.'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $full
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -cne $pin.publisherThumbprint -or
        $signature.SignerCertificate.Subject -cne $pin.publisherSubject) {
        throw 'WebView2 installer must have the approved valid Microsoft signature.'
    }
    # Check the bytes again after signature validation; callers verify the staged copy too.
    if ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pin.sha256) {
        throw 'WebView2 installer changed during validation.'
    }
    [pscustomobject]@{ Path = $full; SizeBytes = [long]$pin.sizeBytes; Sha256 = $pin.sha256
        PublisherThumbprint = $pin.publisherThumbprint; RelativePath = $pin.packageRelativePath }
}

Export-ModuleMember -Function Get-ApprovedWebView2Installer, Test-ApprovedWebView2Installer
