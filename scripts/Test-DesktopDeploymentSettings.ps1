param(
    [Parameter(Mandatory = $true)][string]$SettingsPath,
    [Parameter(Mandatory = $true)][string]$ExpectedServerBaseUrl,
    [switch]$AllowDevelopmentServer,
    [switch]$AllowTransitionalSql,
    [switch]$RequireTransitionalSql
)

$ErrorActionPreference = 'Stop'
try {
    $settings = Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
} catch {
    throw 'The desktop deployment configuration could not be read as JSON.'
}
$server = $null
$expected = $null
if (-not [Uri]::TryCreate([string]$settings.Server.BaseUrl, [UriKind]::Absolute, [ref]$server) -or
    $server.Scheme -ne 'https' -or $server.UserInfo -or $server.Query -or $server.Fragment) {
    throw 'Desktop Server.BaseUrl must be an HTTPS base URL without credentials, query or fragment.'
}
if (-not [Uri]::TryCreate($ExpectedServerBaseUrl, [UriKind]::Absolute, [ref]$expected) -or
    $server.AbsoluteUri.TrimEnd('/') -cne $expected.AbsoluteUri.TrimEnd('/')) {
    throw 'Desktop and installer must target the same server base URL.'
}
if (-not $AllowDevelopmentServer -and $server.IsLoopback) {
    throw 'A customer package must not target a loopback development server.'
}
if ([string]::IsNullOrWhiteSpace([string]$settings.Storage.WorkspaceRoot) -or
    [string]$settings.Storage.WorkspaceRoot -notmatch '^%LOCALAPPDATA%[\\/]') {
    throw 'The packaged workspace must use a per-user LOCALAPPDATA directory.'
}
$workspaceSuffix = ([string]$settings.Storage.WorkspaceRoot).Substring('%LOCALAPPDATA%'.Length)
if ($workspaceSuffix -match '(^|[\\/])\.\.([\\/]|$)|[:%]') {
    throw 'The packaged workspace must stay inside the per-user LOCALAPPDATA directory.'
}
foreach ($entry in @(@('FfmpegPath', 'tools/ffmpeg/ffmpeg.exe'), @('FfprobePath', 'tools/ffmpeg/ffprobe.exe'))) {
    $value = [string]$settings.MediaTools.($entry[0])
    if ($value.Replace('\', '/') -cne $entry[1]) {
        throw 'Packaged media tools must use the approved paths relative to the application.'
    }
}
foreach ($name in @('ComponentRoot', 'TemporaryRoot')) {
    if (-not [string]::IsNullOrWhiteSpace([string]$settings.LocalVoice.$name)) {
        throw 'The release configuration must not embed private local voice directories.'
    }
}
$connection = [string]$settings.Database.ConnectionString
if ($RequireTransitionalSql -and [string]::IsNullOrWhiteSpace($connection)) {
    throw 'This desktop build still requires workflow SQL. Removing its connection setting does not enable API-only operation.'
}
if (-not [string]::IsNullOrWhiteSpace($connection)) {
    if (-not $AllowTransitionalSql) {
        throw 'The package still requires direct SQL; migrate workflow to the API or explicitly select the transitional deployment profile.'
    }
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder.set_ConnectionString($connection)
    foreach ($key in @('password', 'pwd')) {
        if ($builder.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace([string]$builder[$key])) {
            throw 'A shared SQL password must not be embedded in the desktop package.'
        }
    }
    foreach ($key in @('data source', 'server', 'address', 'addr', 'network address')) {
        if ($builder.ContainsKey($key) -and [string]$builder[$key] -match '(?i)(^|[\\,])DUNGDEV($|[\\,])') {
            throw 'The package still targets the development SQL host.'
        }
    }
}
function Assert-NoEmbeddedSecrets($node) {
    if ($null -eq $node -or $node -is [string] -or $node -is [ValueType]) { return }
    if ($node -is [System.Collections.IEnumerable] -and $node -isnot [pscustomobject]) {
        foreach ($item in $node) { Assert-NoEmbeddedSecrets $item }
        return
    }
    foreach ($property in $node.PSObject.Properties) {
        if ($property.Name -match '(?i)^(ApiKey|AccessToken|RefreshToken|ClientSecret|SigningKey|Password)$' -and
            -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw 'The desktop configuration contains a secret field that must not be packaged.'
        }
        Assert-NoEmbeddedSecrets $property.Value
    }
}
Assert-NoEmbeddedSecrets $settings
[pscustomobject]@{
    Valid = $true
    TransitionalSql = -not [string]::IsNullOrWhiteSpace($connection)
    LocalTranslationEnabled = [bool]$settings.Features.VietsubLocalTranslationEnabled
}
