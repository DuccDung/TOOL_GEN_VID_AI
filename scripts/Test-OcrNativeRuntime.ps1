#requires -Version 5.1
param([string]$BundleDirectory = '')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$definition = Get-Content -LiteralPath (Join-Path $repo 'third_party/ocr/MSVC_RUNTIME.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $BundleDirectory) { $BundleDirectory = Join-Path $repo ('artifacts/ocr-native-runtime/msvc-' + $definition.version + '-win-x64') }
foreach ($entry in $definition.files) {
    $path = Join-Path $BundleDirectory $entry.name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw ('Missing OCR runtime: ' + $entry.name + '. Run scripts/Prepare-OcrNativeRuntime.ps1 with the pinned Microsoft installer.')
    }
    $item = Get-Item -LiteralPath $path
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -ne $entry.size -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw ('OCR runtime checksum mismatch: ' + $entry.name)
    }
}
[pscustomobject]@{ Version = $definition.version; FilesVerified = $definition.files.Count; HashesVerified = $true }
