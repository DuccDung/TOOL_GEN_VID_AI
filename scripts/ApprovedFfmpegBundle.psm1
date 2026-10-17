Set-StrictMode -Version Latest

function Assert-ApprovedFfmpegPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $current = [System.IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($current)) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "FFmpeg release paths must not contain links or reparse points: $current"
            }
        }
        $parent = [System.IO.Path]::GetDirectoryName($current.TrimEnd('\', '/'))
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Get-ApprovedFfmpegDefinition {
    param([Parameter(Mandatory = $true)][string]$DefinitionPath)

    $resolvedDefinition = [System.IO.Path]::GetFullPath($DefinitionPath)
    Assert-ApprovedFfmpegPath $resolvedDefinition
    if (-not (Test-Path -LiteralPath $resolvedDefinition -PathType Leaf)) {
        throw 'The pinned FFmpeg technical release definition is missing. See third_party/ffmpeg/RELEASE_REVIEW.md; do not relabel the Development bundle.'
    }
    $definition = Get-Content -LiteralPath $resolvedDefinition -Raw -Encoding utf8 | ConvertFrom-Json
    foreach ($name in @('schemaVersion', 'bundleId', 'version', 'architecture', 'approvalScope', 'approvalKind', 'correspondingSourceComplete', 'redistributionQualified', 'productionQualified', 'files')) {
        if ($null -eq $definition.PSObject.Properties[$name]) { throw "FFmpeg release definition is missing $name." }
    }
    if ($definition.schemaVersion -ne 1 -or $definition.bundleId -notmatch '^[a-z0-9][a-z0-9.-]{0,79}$' -or
        $definition.architecture -ne 'win-x64' -or $definition.approvalScope -ne 'Release' -or
        $definition.approvalKind -ne 'TechnicalPackaging' -or [string]::IsNullOrWhiteSpace($definition.version) -or $definition.version -match '[\r\n]') {
        throw 'FFmpeg release definition has an unsupported identity or approval kind.'
    }
    foreach ($name in @('correspondingSourceComplete', 'redistributionQualified', 'productionQualified')) {
        if ($definition.$name -isnot [bool]) { throw "FFmpeg $name must be an explicit boolean; technical packaging must not imply redistribution qualification." }
    }

    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @($definition.files)) {
        foreach ($name in @('path', 'source', 'sourcePath', 'sha256')) {
            if ($null -eq $file.PSObject.Properties[$name]) { throw "FFmpeg release file is missing $name." }
        }
        if ($file.path -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,119}$' -or $file.path.EndsWith('.') -or
            $file.path -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)' -or -not $names.Add($file.path)) {
            throw "FFmpeg release definition contains an unsafe or duplicate output path: $($file.path)"
        }
        if ($file.source -cnotin @('binary', 'review') -or $file.sha256 -cnotmatch '^[a-f0-9]{64}$') {
            throw 'FFmpeg release definition contains an unsupported file source or SHA-256.'
        }
        if ([string]::IsNullOrWhiteSpace($file.sourcePath) -or [System.IO.Path]::IsPathRooted($file.sourcePath) -or
            $file.sourcePath.Contains(':') -or ($file.sourcePath -split '[/\\]' | Where-Object { $_ -in @('', '.', '..') })) {
            throw 'FFmpeg release definition contains an unsafe source path.'
        }
        if ($file.source -eq 'binary' -and $file.sourcePath -notin @('ffmpeg.exe', 'ffprobe.exe', 'LICENSE.txt')) {
            throw 'Only the pinned FFmpeg binaries and license may be read from the binary source directory.'
        }
    }
    foreach ($required in @('ffmpeg.exe', 'ffprobe.exe', 'LICENSE.txt', 'PROVENANCE.md', 'checksums.sha256', 'SOURCE-AVAILABILITY.md')) {
        if (-not $names.Contains($required)) { throw "FFmpeg release definition is missing the required file $required." }
    }
    foreach ($name in @('ffmpeg.exe', 'ffprobe.exe', 'LICENSE.txt')) {
        $file = @($definition.files | Where-Object { $_.path -eq $name })[0]
        if ($file.source -ne 'binary' -or $file.sourcePath -ne $name) { throw "FFmpeg $name must use the pinned original binary source." }
    }
    return $definition
}

function Test-ApprovedFfmpegFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)]$Definition
    )

    $resolvedDirectory = [System.IO.Path]::GetFullPath($Directory)
    Assert-ApprovedFfmpegPath $resolvedDirectory
    if (-not (Test-Path -LiteralPath $resolvedDirectory -PathType Container)) { throw 'The approved FFmpeg release directory does not exist.' }
    $children = @(Get-ChildItem -LiteralPath $resolvedDirectory -Force)
    if ($children.Count -ne @($Definition.files).Count -or @($children | Where-Object { $_.PSIsContainer }).Count -ne 0) {
        throw 'The FFmpeg release directory has missing or unexpected files; existing bundles are never overwritten.'
    }
    foreach ($file in @($Definition.files)) {
        $path = Join-Path $resolvedDirectory $file.path
        Assert-ApprovedFfmpegPath $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0 -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) {
            throw "The pinned FFmpeg release checksum does not match $($file.path); existing bundles are never overwritten."
        }
    }
}

function Get-ApprovedFfmpegSourceFiles {
    param(
        [Parameter(Mandatory = $true)][string]$BinarySourceDirectory,
        [Parameter(Mandatory = $true)][string]$DefinitionDirectory,
        [Parameter(Mandatory = $true)]$Definition
    )

    foreach ($file in @($Definition.files)) {
        $root = if ($file.source -eq 'binary') { $BinarySourceDirectory } else { $DefinitionDirectory }
        $resolvedRoot = [System.IO.Path]::GetFullPath($root).TrimEnd('\', '/')
        $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $file.sourcePath))
        if (-not $sourcePath.StartsWith($resolvedRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'FFmpeg release source must remain inside its approved source directory.'
        }
        Assert-ApprovedFfmpegPath $sourcePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf) -or (Get-Item -LiteralPath $sourcePath).Length -le 0 -or
            (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) {
            throw "The approved FFmpeg source is missing or its SHA-256 changed: $($file.path)"
        }
        [pscustomobject]@{ Name = [string]$file.path; SourcePath = $sourcePath }
    }
}

Export-ModuleMember -Function Assert-ApprovedFfmpegPath, Get-ApprovedFfmpegDefinition, Test-ApprovedFfmpegFiles, Get-ApprovedFfmpegSourceFiles
