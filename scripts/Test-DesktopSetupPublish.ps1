param(
    [Parameter(Mandatory = $true)][string]$PublishRoot,
    [string]$SourceWebRoot = (Join-Path $PSScriptRoot '..\TOOL-LOCAL\Web\dist')
)

$ErrorActionPreference = 'Stop'
$candidateRoot = [System.IO.Path]::GetFullPath($PublishRoot)
$sourceRoot = [System.IO.Path]::GetFullPath($SourceWebRoot)
$required = @(
    'TOOL-LOCAL.exe', 'workers\piper_worker.py', 'workers\piper-requirements.lock',
    'setup-fixtures\en.png', 'setup-fixtures\zh.png',
    'Sdcb.PaddleOCR.Models.LocalV5.dll', 'paddle_inference_c.dll', 'OpenCvSharpExtern.dll',
    'tools\ffmpeg\ffmpeg.exe', 'tools\ffmpeg\ffprobe.exe', 'tools\ffmpeg\checksums.sha256',
    '_translation_worker\VideoMaker.Vietsub.TranslationWorker.exe',
    '_translation_worker\VideoMaker.Vietsub.TranslationWorker.dll', 'wwwroot\index.html'
)
# Managed OCR assemblies may be inside a single-file desktop publish.
if (-not (Test-Path -LiteralPath (Join-Path $candidateRoot 'TOOL-LOCAL.dll'))) {
    $required = @($required | Where-Object { $_ -ne 'Sdcb.PaddleOCR.Models.LocalV5.dll' })
}
foreach ($relative in $required) {
    $file = Join-Path $candidateRoot $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Publish candidate is missing a required Setup component: $relative"
    }
}
if (Test-Path -LiteralPath (Join-Path $candidateRoot 'appsettings.user.json')) {
    throw 'Publish candidate includes private machine configuration.'
}
$webRoot = Join-Path $candidateRoot 'wwwroot'
$index = Get-Content -LiteralPath (Join-Path $webRoot 'index.html') -Raw -Encoding UTF8
$references = [regex]::Matches($index, '(?:src|href)="(?<path>/assets/[^"]+)"')
if ($references.Count -lt 2) { throw 'Publish index must reference the built JavaScript and CSS assets.' }
foreach ($reference in $references) {
    $relative = [Uri]::UnescapeDataString($reference.Groups['path'].Value).TrimStart('/')
    $target = [System.IO.Path]::GetFullPath((Join-Path $webRoot $relative))
    if (-not $target.StartsWith($webRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publish index references an asset outside wwwroot.'
    }
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Publish index references a missing asset: $relative" }
}
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File)
foreach ($source in $sourceFiles) {
    $relative = $source.FullName.Substring($sourceRoot.Length).TrimStart([char[]]@('\', '/'))
    $target = Join-Path $webRoot $relative
    if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or
        (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
        throw "Published web asset does not match the current build: $relative"
    }
}
[pscustomobject]@{ RequiredComponents = $required.Count; WebAssets = $sourceFiles.Count; MissingOrMismatched = 0 }
