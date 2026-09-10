param([switch]$ServerOnly)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = Split-Path -Parent $PSScriptRoot
$binaryRoot = Join-Path $repo 'TOOL-LOCAL/bin/Release/net10.0-windows/win-x64'
$desktop = Join-Path $binaryRoot 'TOOL-LOCAL.exe'
if (-not (Test-Path -LiteralPath $desktop -PathType Leaf)) { throw 'Build Release first.' }

$settings = Get-Content -LiteralPath (Join-Path $binaryRoot 'appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$baseUrl = $settings.Server.BaseUrl
$overrideFile = Join-Path $binaryRoot 'appsettings.user.json'
if (Test-Path -LiteralPath $overrideFile) {
    $overrides = Get-Content -LiteralPath $overrideFile -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($overrides.Server.BaseUrl) { $baseUrl = $overrides.Server.BaseUrl }
}
$serverUri = [Uri]$baseUrl
if ($serverUri.Scheme -ne 'https' -or -not $serverUri.IsLoopback -or $serverUri.UserInfo -or
    $serverUri.AbsolutePath -ne '/' -or $serverUri.Query -or $serverUri.Fragment) {
    throw 'This launcher requires the local HTTPS acceptance server.'
}
$serverExe = [IO.Path]::GetFullPath((Join-Path $repo 'TOOL-SERVER/bin/Release/net10.0/TOOL-SERVER.exe'))
if (-not (Test-Path -LiteralPath $serverExe -PathType Leaf)) { throw 'Build the Release server first.' }

function Read-ConnectionOverride([string]$Path) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $config = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($config.'ConnectionStrings:VideoFactory') { return [string]$config.'ConnectionStrings:VideoFactory' }
        if ($config.ConnectionStrings.VideoFactory) { return [string]$config.ConnectionStrings.VideoFactory }
    }
}

function Test-ServerSchema {
    $sql = $null
    try {
        $serverRoot = Join-Path $repo 'TOOL-SERVER'
        $connection = Read-ConnectionOverride (Join-Path $serverRoot 'appsettings.json')
        $development = Read-ConnectionOverride (Join-Path $serverRoot 'appsettings.Development.json')
        if ($development) { $connection = $development }
        $project = [xml][IO.File]::ReadAllText((Join-Path $serverRoot 'TOOL-SERVER.csproj'))
        $secretId = $project.SelectSingleNode('//UserSecretsId')
        if ($secretId) {
            $secretOverride = Read-ConnectionOverride (Join-Path $env:APPDATA ('Microsoft/UserSecrets/' + $secretId.InnerText + '/secrets.json'))
            if ($secretOverride) { $connection = $secretOverride }
        }
        foreach ($key in @('ConnectionStrings__VideoFactory', 'ConnectionStrings:VideoFactory')) {
            $override = [Environment]::GetEnvironmentVariable($key)
            if ($override) { $connection = $override }
        }
        # Read-only preflight using the server configuration; never display credentials or apply SQL migrations.
        $normalized = $connection -replace '(?i)Trust Server Certificate', 'TrustServerCertificate' -replace '(?i)(^|;)\s*Command Timeout\s*=[^;]*', ''
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($normalized)
        $builder['Connect Timeout'] = 8
        $sql = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
        $sql.Open()
        $query = $sql.CreateCommand()
        $query.CommandTimeout = 8
        $query.CommandText = @'
SELECT CASE WHEN COL_LENGTH('vf.Projects', 'LocalVoicePolicyVersion') IS NOT NULL
 AND EXISTS (SELECT 1 FROM ai.SchemaVersions WHERE Version='4.1.8-local-voice-consistency')
 AND EXISTS (SELECT 1 FROM ai.SchemaVersions WHERE Version='4.1.8-tiktok-multi-account')
 THEN 1 ELSE 0 END
'@
        if ([int]$query.ExecuteScalar() -ne 1) { throw 'Required schema is missing.' }
    } catch {
        throw 'Server database preflight failed. Verify its connection and schema before starting; no migration was run.'
    } finally { if ($sql) { $sql.Dispose() } }
}

function Assert-ServerOwner($Listeners) {
    foreach ($ownerId in @($Listeners.OwningProcess | Select-Object -Unique)) {
        $owner = Get-Process -Id $ownerId -ErrorAction Stop
        if (-not $owner.Path -or -not $owner.Path.Equals($serverExe, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The configured port belongs to another server build. Close that server or choose another port in the user configuration.'
        }
    }
}

$mutex = New-Object Threading.Mutex($false, ('Local\VideoMaker.LocalVoice.Start.' + $serverUri.Port))
$locked = $false
try {
    try { $locked = $mutex.WaitOne([TimeSpan]::FromSeconds(30)) }
    catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'Another launcher is preparing the server. Please try again shortly.' }
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $serverUri.Port -ErrorAction SilentlyContinue)
    $started = $null
    if ($listeners.Count -eq 0) {
        Test-ServerSchema
        $logRoot = Join-Path $repo 'artifacts/local-voice-server'
        New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
        $runId = [Guid]::NewGuid().ToString('N')
        $previousAspnet = $env:ASPNETCORE_ENVIRONMENT
        $previousDotnet = $env:DOTNET_ENVIRONMENT
        try {
            $env:ASPNETCORE_ENVIRONMENT = 'Development'
            $env:DOTNET_ENVIRONMENT = 'Development'
            $started = Start-Process -FilePath $serverExe -WorkingDirectory (Join-Path $repo 'TOOL-SERVER') `
                -ArgumentList @('--urls', $serverUri.GetLeftPart([UriPartial]::Authority), '--Logging:LogLevel:Microsoft.EntityFrameworkCore=Warning') `
                -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logRoot "$runId.stdout.log") `
                -RedirectStandardError (Join-Path $logRoot "$runId.stderr.log") -PassThru
            [pscustomobject]@{ ProcessId = $started.Id; StartedAtUtc = [DateTime]::UtcNow.ToString('o'); Url = $baseUrl } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $logRoot 'server.json') -Encoding UTF8
        } finally {
            $env:ASPNETCORE_ENVIRONMENT = $previousAspnet
            $env:DOTNET_ENVIRONMENT = $previousDotnet
        }
    } else { Assert-ServerOwner $listeners }

    Add-Type -AssemblyName System.Net.Http
    $handler = New-Object Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $handler.UseProxy = $false
    $client = New-Object Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(2)
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    try {
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($started -and $started.HasExited) { throw 'Server startup failed. See artifacts/local-voice-server for diagnostics.' }
            try {
                # Malformed JSON must fail model binding with 400 before authentication; no credential is sent.
                $content = New-Object Net.Http.StringContent('{', [Text.Encoding]::UTF8, 'application/json')
                try {
                    $response = $client.PostAsync([Uri]::new($serverUri, 'api/auth/login'), $content).GetAwaiter().GetResult()
                    try { $ready = [int]$response.StatusCode -eq 400 }
                    finally { $response.Dispose() }
                } finally { $content.Dispose() }
            } catch { $ready = $false }
            if ($ready) { break }
            Start-Sleep -Milliseconds 500
        }
    } finally { $client.Dispose() }
    if (-not $ready) { throw 'The login endpoint is not ready over HTTPS. Check the server logs and trusted localhost certificate.' }
    Assert-ServerOwner @(Get-NetTCPConnection -State Listen -LocalPort $serverUri.Port -ErrorAction Stop)
    Write-Host "Account Server ready: $baseUrl"
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
if ($ServerOnly) { return }
& dotnet (Join-Path $binaryRoot 'TOOL-LOCAL.dll') --check-local-voice
if ($LASTEXITCODE -ne 0) { throw 'Voice runtime is not READY. Run scripts/Prepare-VeoLocalVoice.ps1 -Mode Prepare.' }
if (@(Get-Process -Name TOOL-LOCAL -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $desktop }).Count -gt 0) {
    Write-Host 'VideoMaker is already open.'
    return
}
# Interactive application requested by the user; this is not a background helper.
Start-Process -FilePath $desktop -WorkingDirectory $binaryRoot
