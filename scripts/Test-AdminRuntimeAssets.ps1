[CmdletBinding()]
param(
    [Parameter()]
    [uri]$AdminUrl = 'https://localhost:7202/admin',

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedMarker = 'admin-only-new-20261014.1',

    [Parameter()]
    [switch]$SkipCertificateCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Throw-RuntimeMismatch {
    param([Parameter(Mandatory)][string]$Detail)

    throw "Wrong build/checkout or stale cache at '$($AdminUrl.AbsoluteUri)'. $Detail"
}

function Invoke-ReadOnlyRequest {
    param([Parameter(Mandatory)][uri]$Uri)

    $parameters = @{
        Uri = $Uri
        Method = 'Get'
        MaximumRedirection = 3
        ErrorAction = 'Stop'
    }

    $command = Get-Command Invoke-WebRequest
    if ($command.Parameters.ContainsKey('UseBasicParsing')) {
        $parameters.UseBasicParsing = $true
    }
    if ($SkipCertificateCheck) {
        if (-not $command.Parameters.ContainsKey('SkipCertificateCheck')) {
            throw 'The -SkipCertificateCheck option requires PowerShell 7 or later.'
        }
        $parameters.SkipCertificateCheck = $true
    }

    Invoke-WebRequest @parameters
}

function Get-AssetUri {
    param(
        [Parameter(Mandatory)][string]$Html,
        [Parameter(Mandatory)][ValidateSet('href', 'src')][string]$Attribute,
        [Parameter(Mandatory)][string]$ExpectedPath
    )

    $pattern = '\b' + [regex]::Escape($Attribute) + '\s*=\s*["''](?<value>[^"'']+)["'']'
    foreach ($match in [regex]::Matches($Html, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        $reference = [System.Net.WebUtility]::HtmlDecode($match.Groups['value'].Value)
        $candidate = [uri]::new($AdminUrl, $reference)
        $extension = [System.IO.Path]::GetExtension($ExpectedPath)
        $stem = $ExpectedPath.Substring(0, $ExpectedPath.Length - $extension.Length)
        $fingerprintPattern = '^' + [regex]::Escape($stem) + '\.[a-z0-9_-]{8,}' + [regex]::Escape($extension) + '$'
        $isFingerprint = $candidate.AbsolutePath -match $fingerprintPattern
        if ($candidate.AbsolutePath.Equals($ExpectedPath, [System.StringComparison]::OrdinalIgnoreCase) -or $isFingerprint) {
            if (-not $candidate.IsLoopback -or $candidate.Authority -ne $AdminUrl.Authority) {
                Throw-RuntimeMismatch "Asset '$ExpectedPath' must be served by the same local origin."
            }
            if (-not $isFingerprint -and $candidate.Query -notmatch '(^\?|&)v=[^&]+') {
                Throw-RuntimeMismatch "Asset '$ExpectedPath' must have a fingerprint or cache-busting 'v' query."
            }
            return $candidate
        }
    }

    Throw-RuntimeMismatch "HTML does not reference asset '$ExpectedPath'."
}

if (-not $AdminUrl.IsAbsoluteUri -or $AdminUrl.Scheme -notin @('http', 'https')) {
    throw 'AdminUrl must be an absolute HTTP/HTTPS URL.'
}
if (-not $AdminUrl.IsLoopback) {
    throw 'This diagnostic only allows localhost/loopback URLs.'
}
if (-not $AdminUrl.AbsolutePath.TrimEnd('/').Equals('/admin', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "AdminUrl must point to the '/admin' path."
}

try {
    $htmlResponse = Invoke-ReadOnlyRequest -Uri $AdminUrl
}
catch {
    throw "Cannot read '$($AdminUrl.AbsoluteUri)'. Confirm that the intended server is running. $($_.Exception.Message)"
}

$html = [string]$htmlResponse.Content
$markerMatch = [regex]::Match(
    $html,
    '\bdata-admin-ui-build\s*=\s*["''](?<marker>[^"'']+)["'']',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$actualMarker = if ($markerMatch.Success) { $markerMatch.Groups['marker'].Value } else { $null }
if ($actualMarker -ne $ExpectedMarker) {
    $actualLabel = if ($actualMarker) { "'$actualMarker'" } else { 'no marker' }
    Throw-RuntimeMismatch "Expected marker '$ExpectedMarker', but the response has $actualLabel."
}
if ($html -notmatch '\bname\s*=\s*["'']videomaker-admin-ui-build["'']' -or
    $html -notmatch '\bid\s*=\s*["'']adminUiBuildMarker["'']') {
    Throw-RuntimeMismatch 'HTML is missing the meta marker or the visible sidebar marker.'
}

$cacheControl = [string]$htmlResponse.Headers['Cache-Control']
if ($cacheControl -notmatch 'no-store') {
    Throw-RuntimeMismatch "HTML /admin is missing Cache-Control no-store (actual: '$cacheControl')."
}

$cssUri = Get-AssetUri -Html $html -Attribute href -ExpectedPath '/admin/admin.css'
$adminScriptUri = Get-AssetUri -Html $html -Attribute src -ExpectedPath '/admin/admin.js'
$organizationScriptUri = Get-AssetUri -Html $html -Attribute src -ExpectedPath '/admin/admin-organizations.js'
$composerCssUri = Get-AssetUri -Html $html -Attribute href -ExpectedPath '/admin/admin-plan-composer.css'
$composerScriptUri = Get-AssetUri -Html $html -Attribute src -ExpectedPath '/admin/admin-plan-composer.js'
$pagesScriptUri = Get-AssetUri -Html $html -Attribute src -ExpectedPath '/admin/admin-pages.js'
$dialogsScriptUri = Get-AssetUri -Html $html -Attribute src -ExpectedPath '/admin/admin-dialogs.js'
$modulesCssUri = Get-AssetUri -Html $html -Attribute href -ExpectedPath '/admin/admin-modules.css'
$setupCssUri = Get-AssetUri -Html $html -Attribute href -ExpectedPath '/admin/admin-setup-wizard.css'
$setupScriptUri = Get-AssetUri -Html $html -Attribute src -ExpectedPath '/admin/admin-setup-wizard.js'

$css = [string](Invoke-ReadOnlyRequest -Uri $cssUri).Content
$adminScript = [string](Invoke-ReadOnlyRequest -Uri $adminScriptUri).Content
$organizationScript = [string](Invoke-ReadOnlyRequest -Uri $organizationScriptUri).Content
$composerCss = [string](Invoke-ReadOnlyRequest -Uri $composerCssUri).Content
$composerScript = [string](Invoke-ReadOnlyRequest -Uri $composerScriptUri).Content
$pagesScript = [string](Invoke-ReadOnlyRequest -Uri $pagesScriptUri).Content
$dialogsScript = [string](Invoke-ReadOnlyRequest -Uri $dialogsScriptUri).Content
$css += [string](Invoke-ReadOnlyRequest -Uri $modulesCssUri).Content
$setupCss = [string](Invoke-ReadOnlyRequest -Uri $setupCssUri).Content
$setupScript = [string](Invoke-ReadOnlyRequest -Uri $setupScriptUri).Content
foreach ($token in @('admin-setup-20261014.1', '/api/admin/setup', 'data-sepay', 'data-reconcile', 'configuration')) {
    if ($setupScript.IndexOf($token, [System.StringComparison]::Ordinal) -lt 0) {
        Throw-RuntimeMismatch "Served setup wizard is missing '$token'."
    }
}
foreach ($selector in @('.setup-columns', '.setup-steps', '.setup-footer', '.setup-summary')) {
    if ($setupCss.IndexOf($selector, [System.StringComparison]::Ordinal) -lt 0) {
        Throw-RuntimeMismatch "Served setup CSS is missing '$selector'."
    }
}

$requiredCssSelectors = @(
    '.admin-build-marker',
    '.setup-center-hero',
    '.setup-progress',
    '.setup-stage-list',
    '.setup-stage-number',
    '.setup-center-note',
    '.organization-setup-hero',
    '.organization-setup-steps',
    '.policy-focus-layout',
    '.video-policy-card',
    '.admin-disclosure',
    '.pricing-provider-summary',
    '.rate-preview',
    '.pool-list-summary',
    '.pool-detail-panel.standalone',
    '.pool-back-button',
    '.pool-members',
    '.readiness-explanation'
)
foreach ($selector in $requiredCssSelectors) {
    if ($css.IndexOf($selector, [System.StringComparison]::Ordinal) -lt 0) {
        Throw-RuntimeMismatch "Served CSS is missing selector '$selector'."
    }
}
foreach ($rule in @('prefers-reduced-motion: reduce', 'forced-colors: active')) {
    if ($css.IndexOf($rule, [System.StringComparison]::Ordinal) -lt 0) {
        Throw-RuntimeMismatch "Served CSS is missing accessibility rule '$rule'."
    }
}

foreach ($token in @('returnToSetupButton', 'videoMakerAdminShell')) {
    if ($adminScript.IndexOf($token, [System.StringComparison]::Ordinal) -lt 0) {
        Throw-RuntimeMismatch "Served admin.js is missing token '$token'."
    }
}
foreach ($token in @('setup-center-hero', 'role="progressbar"', 'data-setup-next', 'pool-list-summary', 'pool-detail-panel standalone')) {
    if (($organizationScript + $pagesScript).IndexOf($token, [System.StringComparison]::Ordinal) -lt 0) {
        Throw-RuntimeMismatch "Served admin-organizations.js is missing token '$token'."
    }
}
if ($html -notmatch 'data-admin-ui-generation="2"' -or $html -notmatch 'id="homeLaunch"' -or $html -match 'id="userOverview"|id="planOverview"|license-overview-grid') {
    Throw-RuntimeMismatch 'HTML must contain only the new default Admin layout.'
}
if ($pagesScript -notmatch 'function overview' -or $dialogsScript -notmatch 'videoMakerAdminDialogs') {
    Throw-RuntimeMismatch 'New page and shared dialog modules were not served.'
}

foreach ($token in @('function priceValue', 'function formatPrice', 'status-check')) {
    if ($composerScript.IndexOf($token, [System.StringComparison]::Ordinal) -lt 0) {
        Throw-RuntimeMismatch "Served composer script is missing '$token'."
    }
}
if ($composerCss -notmatch '\.plan-composer\s*\{\s*width:\s*100%') {
    Throw-RuntimeMismatch 'Served composer CSS still has the previous constrained alignment.'
}
foreach ($fontPath in @('/admin/fonts/roboto-latin.woff2', '/admin/fonts/roboto-vietnamese.woff2')) {
    if ($css.IndexOf($fontPath, [System.StringComparison]::Ordinal) -lt 0) {
        Throw-RuntimeMismatch "Served CSS does not reference local font '$fontPath'."
    }
    $fontResponse = Invoke-ReadOnlyRequest -Uri ([uri]::new($AdminUrl, $fontPath))
    if ([string]$fontResponse.Headers['Content-Type'] -notmatch 'font/woff2|application/font-woff') {
        Throw-RuntimeMismatch "Local font '$fontPath' was not served as WOFF2."
    }
}

[pscustomobject]@{
    Result = 'OK'
    AdminUrl = $AdminUrl.AbsoluteUri
    Marker = $actualMarker
    HtmlCacheControl = $cacheControl
    CssUrl = $cssUri.AbsoluteUri
    ComposerCssUrl = $composerCssUri.AbsoluteUri
    SetupCssUrl = $setupCssUri.AbsoluteUri
    JavaScriptUrls = @($adminScriptUri.AbsoluteUri, $organizationScriptUri.AbsoluteUri, $composerScriptUri.AbsoluteUri, $pagesScriptUri.AbsoluteUri, $dialogsScriptUri.AbsoluteUri, $setupScriptUri.AbsoluteUri)
    LocalFonts = 'Roboto Latin + Vietnamese'
}
