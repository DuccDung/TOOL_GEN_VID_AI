param(
    [string]$OutputRoot = '',
    [switch]$SkipArchive
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'TOOL-SERVER\TOOL-SERVER.csproj'
Import-Module (Join-Path $PSScriptRoot 'ServerPackageSafety.psm1') -Force
$artifactRoot = Join-Path $repoRoot 'artifacts'
$packageId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$packageName = 'TOOL-SERVER-IIS-dll-runtime-win-x64-{0}-{1}' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'), $packageId
$publishDir = Join-Path $artifactRoot ('server-' + $packageId)
$zipPath = Join-Path $artifactRoot ($packageName + '.zip')

if ($OutputRoot) {
    if (-not [IO.Path]::IsPathRooted($OutputRoot) -or $OutputRoot -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+[\\/])') {
        throw 'OutputRoot must be an absolute directory path.'
    }
    $publishDir = [IO.Path]::GetFullPath($OutputRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ($publishDir.Equals([IO.Path]::GetPathRoot($publishDir).TrimEnd([IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputRoot must not be a drive or share root.'
    }
    $zipPath = Join-Path (Split-Path -Parent $publishDir) ($packageName + '.zip')
}
if (Test-Path -LiteralPath $publishDir) { throw 'Publish directory already exists; use a new directory.' }
$ancestor = [IO.DirectoryInfo]::new((Split-Path -Parent $publishDir))
while ($null -ne $ancestor) {
    if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Publish directory must not pass through a reparse point.'
    }
    $ancestor = $ancestor.Parent
}
New-Item -ItemType Directory -Path $publishDir | Out-Null

& dotnet publish $projectPath -c Release -r win-x64 --self-contained false -o $publishDir /p:UseAppHost=false /p:AspNetCoreHostingModel=OutOfProcess | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

# Sanitize immediately, before the runtime copy, so an interrupted package has no credentials.
Get-ChildItem -LiteralPath $publishDir -Filter 'appsettings.*.json' -File | ForEach-Object {
    if (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Published settings contain a reparse point.' }
    Remove-Item -LiteralPath $_.FullName
}
Set-ServerPackageSafeSettings -PublishDirectory $publishDir
Assert-ServerPackageSafe -PublishDirectory $publishDir

# Framework-dependent DLL plus a private x64 .NET/ASP.NET Core runtime.
$dotnetExe = (Get-Command dotnet.exe -ErrorAction Stop).Source
$dotnetRoot = Split-Path -Parent $dotnetExe
$netCoreRoot = Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App'
$aspNetRoot = Join-Path $dotnetRoot 'shared\Microsoft.AspNetCore.App'
$fxrRoot = Join-Path $dotnetRoot 'host\fxr'
$runtimeVersion = Get-ChildItem -LiteralPath $netCoreRoot -Directory |
    Where-Object { $_.Name -match '^10\.\d+\.\d+$' -and (Test-Path -LiteralPath (Join-Path $aspNetRoot $_.Name) -PathType Container) -and (Test-Path -LiteralPath (Join-Path $fxrRoot $_.Name) -PathType Container) } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1 -ExpandProperty Name
if (-not $runtimeVersion) {
    throw 'Matching x64 .NET 10, ASP.NET Core 10 and hostfxr runtimes are not installed on this build machine.'
}

$runtimeDir = Join-Path $publishDir 'runtime'
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
Copy-Item -LiteralPath $dotnetExe -Destination $runtimeDir
foreach ($notice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    $noticePath = Join-Path $dotnetRoot $notice
    if (-not (Test-Path -LiteralPath $noticePath -PathType Leaf)) {
        throw "Runtime attribution file $notice is missing."
    }
    Copy-Item -LiteralPath $noticePath -Destination $runtimeDir
}
foreach ($framework in @('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App')) {
    $source = Join-Path (Join-Path $dotnetRoot "shared\$framework") $runtimeVersion
    $destination = Join-Path $runtimeDir "shared\$framework"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Recurse
}
$fxrDestination = Join-Path $runtimeDir 'host\fxr'
New-Item -ItemType Directory -Path $fxrDestination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $fxrRoot $runtimeVersion) -Destination $fxrDestination -Recurse

$webConfigPath = Join-Path $publishDir 'web.config'
$serverDllPath = Join-Path $publishDir 'TOOL-SERVER.dll'
$privateDotnetExePath = Join-Path $runtimeDir 'dotnet.exe'
if (-not (Test-Path -LiteralPath $webConfigPath -PathType Leaf) -or -not (Test-Path -LiteralPath $serverDllPath -PathType Leaf) -or -not (Test-Path -LiteralPath $privateDotnetExePath -PathType Leaf)) {
    throw 'IIS web.config, server DLL or private runtime host is missing from published output.'
}
if (Test-Path -LiteralPath (Join-Path $publishDir 'TOOL-SERVER.exe') -PathType Leaf) {
    throw 'Application executable is still present in published output.'
}
[xml]$webConfig = Get-Content -LiteralPath $webConfigPath -Raw -Encoding UTF8
$serverConfig = $webConfig.configuration.location.'system.webServer'
$serverConfig.aspNetCore.SetAttribute('processPath', '.\runtime\dotnet.exe')
$serverConfig.aspNetCore.SetAttribute('arguments', '.\TOOL-SERVER.dll')
$serverConfig.aspNetCore.SetAttribute('hostingModel', 'outofprocess')
$webConfig.Save($webConfigPath)
if ($serverConfig.aspNetCore.processPath -ne '.\runtime\dotnet.exe' -or $serverConfig.handlers.add.verb -ne '*') {
    throw 'Published IIS handler or private runtime path is invalid.'
}
$allowedVerbs = @($serverConfig.security.requestFiltering.verbs.add | ForEach-Object { $_.verb })
foreach ($verb in @('GET', 'HEAD', 'OPTIONS', 'POST', 'PUT', 'PATCH', 'DELETE')) {
    if ($verb -notin $allowedVerbs) {
        throw "Published web.config does not allow $verb."
    }
}
$privateRuntimeInventory = & $privateDotnetExePath --list-runtimes
if ($LASTEXITCODE -ne 0 -or -not ($privateRuntimeInventory -match "^Microsoft.NETCore.App $([regex]::Escape($runtimeVersion)) ") -or -not ($privateRuntimeInventory -match "^Microsoft.AspNetCore.App $([regex]::Escape($runtimeVersion)) ")) {
    throw 'Private .NET/ASP.NET Core runtime cannot be discovered by bundled dotnet.exe.'
}

Assert-ServerPackageSafe -PublishDirectory $publishDir
$serverDllSha256 = (Get-FileHash -LiteralPath $serverDllPath -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not $SkipArchive) {
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($zipPath + '.sha256') -Value "$hash  $([IO.Path]::GetFileName($zipPath))" -Encoding ascii
    Write-Host "IIS upload ZIP: $zipPath"
    Write-Host "SHA256: $hash"
} else { $zipPath = $null }

Write-Host "Publish folder: $publishDir"
Write-Host "Bundled .NET/ASP.NET Core runtime: $runtimeVersion"
[pscustomobject]@{
    PublishDirectory = $publishDir
    ZipPath = $zipPath
    RuntimeVersion = $runtimeVersion
    ServerDllSha256 = $serverDllSha256
}
