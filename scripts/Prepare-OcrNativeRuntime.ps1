#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [string]$BundleDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$definition = Get-Content -LiteralPath (Join-Path $repo 'third_party/ocr/MSVC_RUNTIME.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $BundleDirectory) { $BundleDirectory = Join-Path $repo ('artifacts/ocr-native-runtime/msvc-' + $definition.version + '-win-x64') }
$BundleDirectory = [IO.Path]::GetFullPath($BundleDirectory)
if ((Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash -ne $definition.installerSha256) {
    throw 'The Microsoft runtime installer does not match the pinned SHA-256.'
}
if (Test-Path -LiteralPath $BundleDirectory) {
    & (Join-Path $PSScriptRoot 'Test-OcrNativeRuntime.ps1') -BundleDirectory $BundleDirectory
    return
}
# The hash above authenticates the fixed CAB offsets. Never execute the installer or MSI.
$stage = $BundleDirectory + '.stage-' + [Guid]::NewGuid().ToString('N')
$payload = Join-Path $stage 'payload'
$attached = Join-Path $stage 'attached'
$minimum = Join-Path $stage 'minimum'
[void](New-Item -ItemType Directory -Path $payload,$attached,$minimum -Force)
$inputStream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $InstallerPath).Path)
try {
    $null = $inputStream.Seek([long]$definition.cabOffset, [IO.SeekOrigin]::Begin)
    $reader = [IO.BinaryReader]::new($inputStream)
    $bytes = $reader.ReadBytes([int]$definition.cabLength)
    if ($bytes.Length -ne $definition.cabLength) { throw 'Truncated Microsoft runtime CAB.' }
    [IO.File]::WriteAllBytes((Join-Path $stage 'attached.cab'), $bytes)
} finally { $inputStream.Dispose() }
$expand = Join-Path ([Environment]::SystemDirectory) 'expand.exe'
& $expand '-F:*' (Join-Path $stage 'attached.cab') $attached | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot extract the Microsoft attached CAB.' }
& $expand '-F:*' (Join-Path $attached 'a4') $minimum | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot extract the Microsoft minimum-runtime CAB.' }
foreach ($entry in $definition.files) {
    Copy-Item -LiteralPath (Join-Path $minimum ($entry.name + '_amd64')) -Destination (Join-Path $payload $entry.name)
}
& (Join-Path $PSScriptRoot 'Test-OcrNativeRuntime.ps1') -BundleDirectory $payload
# Both locations belong to this invocation; preserve extraction evidence and old bundles.
if (![IO.Path]::GetFullPath($payload).StartsWith($stage + '\', [StringComparison]::OrdinalIgnoreCase) -or
    (Get-Item -LiteralPath $payload).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unsafe staging directory.' }
Move-Item -LiteralPath $payload -Destination $BundleDirectory
