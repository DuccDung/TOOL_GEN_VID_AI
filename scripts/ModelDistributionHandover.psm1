#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
Add-Type -AssemblyName System.IO.Compression

function Assert-ModelSafePath([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'Model distribution paths must be absolute.' }
    $full = [IO.Path]::GetFullPath($Path)
    for ($current = $full; $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Model distribution paths cannot traverse reparse points.'
        }
    }
    $full
}

function Assert-ModelRelativePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.StartsWith('/') -or $Path.Contains('\')) {
        throw 'Unsafe model distribution entry path.'
    }
    foreach ($segment in $Path.Split('/')) {
        if (-not $segment -or $segment -in @('.', '..') -or $segment -match '[:<>"|?*\x00-\x1f]' -or
            $segment.EndsWith('.') -or $segment.EndsWith(' ') -or
            $segment -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
            throw 'Unsafe model distribution entry path.'
        }
    }
}

function Get-ModelFileStamp([string]$Path, [switch]$IncludeSha512) {
    $full = Assert-ModelSafePath $Path
    $stream = [IO.File]::Open($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    $sha512 = $null
    try {
        $result = [ordered]@{ sizeBytes = [long]$stream.Length; sha256 = ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
        if ($IncludeSha512) {
            $stream.Position = 0
            $sha512 = [Security.Cryptography.SHA512]::Create()
            $result.sha512Base64 = [Convert]::ToBase64String($sha512.ComputeHash($stream))
        }
        [pscustomobject]$result
    } finally {
        if ($sha512) { $sha512.Dispose() }
        $sha256.Dispose()
        $stream.Dispose()
    }
}

# A read-only source handle is held through verification and copying. No links, overwrite,
# network access or executable/model loading are involved.
function Copy-ModelVerifiedFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][long]$ExpectedSize,
        [string]$ExpectedSha256 = '',
        [string]$ExpectedSha512Base64 = ''
    )
    if ($ExpectedSize -lt 0 -or (-not $ExpectedSha256 -and -not $ExpectedSha512Base64) -or
        ($ExpectedSha256 -and $ExpectedSha256 -cnotmatch '^[0-9a-f]{64}$')) { throw 'Invalid expected model file identity.' }
    $from = Assert-ModelSafePath $Source
    $to = Assert-ModelSafePath $Destination
    if (Test-Path -LiteralPath $to) { throw 'Model distribution destination already exists.' }
    $inputStream = [IO.File]::Open($from, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $outputStream = $null
    $sha256 = [Security.Cryptography.SHA256]::Create()
    $sha512 = $null
    try {
        if ($inputStream.Length -ne $ExpectedSize) { throw 'Model distribution source size mismatch.' }
        $actualSha256 = ([BitConverter]::ToString($sha256.ComputeHash($inputStream))).Replace('-', '').ToLowerInvariant()
        if ($ExpectedSha256 -and $actualSha256 -cne $ExpectedSha256) { throw 'Model distribution source SHA-256 mismatch.' }
        if ($ExpectedSha512Base64) {
            $inputStream.Position = 0
            $sha512 = [Security.Cryptography.SHA512]::Create()
            if ([Convert]::ToBase64String($sha512.ComputeHash($inputStream)) -cne $ExpectedSha512Base64) {
                throw 'Model distribution source SHA-512 mismatch.'
            }
        }
        $inputStream.Position = 0
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to)) | Out-Null
        $null = Assert-ModelSafePath $to
        $outputStream = [IO.File]::Open($to, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $inputStream.CopyTo($outputStream, 1048576)
        $outputStream.Flush($true)
        $outputStream.Dispose()
        $outputStream = $null
        $copied = Get-ModelFileStamp $to -IncludeSha512:([bool]$ExpectedSha512Base64)
        if ($copied.sizeBytes -ne $ExpectedSize -or $copied.sha256 -cne $actualSha256 -or
            ($ExpectedSha512Base64 -and $copied.sha512Base64 -cne $ExpectedSha512Base64)) {
            throw 'Model distribution copy verification failed.'
        }
        $copied
    } finally {
        if ($outputStream) { $outputStream.Dispose() }
        if ($sha512) { $sha512.Dispose() }
        $sha256.Dispose()
        $inputStream.Dispose()
    }
}

function Test-ModelPiperArchive {
    param([Parameter(Mandatory = $true)][string]$ArchivePath, [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][long]$ExpectedExpandedBytes)
    if ($Manifest.schemaVersion -ne 1 -or @($Manifest.files).Count -lt 1 -or
        @($Manifest.files).Count -gt 12000 -or @($Manifest.installedFiles).Count -gt 12000 -or
        $ExpectedExpandedBytes -lt 1) { throw 'Invalid Piper distribution manifest.' }
    $expected = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
    $installed = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    [long]$total = 0
    foreach ($file in $Manifest.files) {
        Assert-ModelRelativePath $file.path
        if ($file.path -match '(^|/)(?:\.venv|__pycache__|probe\.json|ready(?:\.json)?|cache)(/|$)' -or
            $file.size -lt 0 -or $file.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $expected.ContainsKey($file.path) -or
            $file.size -gt $ExpectedExpandedBytes - $total) { throw 'Invalid or duplicate Piper payload entry.' }
        $expected.Add($file.path, $file)
        $total += [long]$file.size
    }
    if ($total -ne $ExpectedExpandedBytes) { throw 'Piper expanded size mismatch.' }
    foreach ($file in $Manifest.installedFiles) {
        Assert-ModelRelativePath $file.path
        if (-not $file.path.StartsWith('.venv/', [StringComparison]::Ordinal) -or $file.size -lt 0 -or
            $file.sha256 -cnotmatch '^[0-9a-f]{64}$' -or -not $installed.Add($file.path)) {
            throw 'Invalid Piper installed-file declaration.'
        }
    }
    # Parent/file collisions must fail even when archive entries have unique full names.
    foreach ($name in @($expected.Keys)) {
        $parent = [IO.Path]::GetDirectoryName($name).Replace('\', '/')
        while ($parent) {
            if ($expected.ContainsKey($parent)) { throw 'Piper file and directory collision.' }
            $parent = [IO.Path]::GetDirectoryName($parent).Replace('\', '/')
        }
    }
    $stream = [IO.File]::Open((Assert-ModelSafePath $ArchivePath), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $archive = $null
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
        if ($archive.Entries.Count -ne $expected.Count) { throw 'Piper archive file count mismatch.' }
        $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $buffer = New-Object byte[] 1048576
        foreach ($entry in $archive.Entries) {
            Assert-ModelRelativePath $entry.FullName
            $kind = (($entry.ExternalAttributes -shr 16) -band 0xf000)
            if ($kind -eq 0xa000 -or ($entry.ExternalAttributes -band 0x400) -or
                -not $seen.Add($entry.FullName) -or -not $expected.ContainsKey($entry.FullName)) {
                throw 'Piper archive contains a link, duplicate or untracked file.'
            }
            $file = $expected[$entry.FullName]
            if ($entry.FullName -cne $file.path -or $entry.Length -ne $file.size) { throw 'Piper archive entry identity mismatch.' }
            $entryStream = $entry.Open()
            [long]$readTotal = 0
            $hash.Initialize()
            try {
                while (($read = $entryStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    if ($read -gt [long]$file.size - $readTotal) { throw 'Piper entry exceeds its declared size.' }
                    $null = $hash.TransformBlock($buffer, 0, $read, $buffer, 0)
                    $readTotal += $read
                }
                $null = $hash.TransformFinalBlock($buffer, 0, 0)
                $actual = ([BitConverter]::ToString($hash.Hash)).Replace('-', '').ToLowerInvariant()
                if ($readTotal -ne $file.size -or $actual -cne $file.sha256) { throw 'Piper archive entry checksum mismatch.' }
            } finally { $entryStream.Dispose() }
        }
        [pscustomobject]@{ filesVerified = $seen.Count; expandedBytes = $total; runtimeProbed = $false }
    } finally {
        $hash.Dispose()
        if ($archive) { $archive.Dispose() }
        $stream.Dispose()
    }
}

function Write-ModelDistributionJson([string]$Path, $Value) {
    $full = Assert-ModelSafePath $Path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full)) | Out-Null
    $writer = New-Object IO.StreamWriter([IO.File]::Open($full, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None), (New-Object Text.UTF8Encoding($false)))
    try { $writer.Write(($Value | ConvertTo-Json -Depth 18)); $writer.WriteLine() } finally { $writer.Dispose() }
}

function Test-ModelSnapshotPath([string]$Path) {
    # Hash identities only; never copy source/config bodies. Restrict scope before reading.
    if ($Path -match '\\|(^|/)(?:bin|obj|\.git|\.codex|\.tmp|output|artifacts|dist|node_modules|packages|cache|\.venv|runtime-test)(/|$)' -or
        $Path -match '(?i)(?:appsettings|secret|credential|token|connectionstring|\.env)' -or
        $Path -notmatch '^(?:TOOL-[^/]+/|scripts/|third_party/(?:translation|voice|ocr|ffmpeg)/|[^/]+\.(?:md|slnx)$)') { return $false }
    if ($Path -match '\.(?:cs|csproj|ps1|psm1|py|ts|tsx|js|cjs|css|html|md|props|targets|slnx|lock|in)$') { return $true }
    return $Path -match '^third_party/(?:translation/COMPONENT_MANIFEST|voice/(?:COMPONENT_MANIFEST|PIPER_OFFLINE_APPROVED)|ocr/MSVC_RUNTIME)\.json$'
}

function Get-ModelHandoverFiles([string]$Root) {
    $pending = New-Object 'Collections.Generic.Stack[string]'
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Model handover contains reparse points.' }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) } else { $entry }
        }
    }
}

function Test-ModelHandoverInventory([Parameter(Mandatory = $true)][string]$Root) {
    $fullRoot = Assert-ModelSafePath $Root
    $inventoryPath = Assert-ModelSafePath (Join-Path $fullRoot 'inventory.json')
    if ((Get-Item -LiteralPath $inventoryPath).Length -gt 16777216) { throw 'Model handover inventory is too large.' }
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($inventory.schemaVersion -ne 1 -or $inventory.state -cne 'ArtifactVerified' -or
        $inventory.candidateId -cnotmatch '^[a-z0-9][a-z0-9.-]{1,100}$' -or @($inventory.files).Count -lt 1 -or
        @($inventory.files).Count -gt 20000 -or @($inventory.excludes).Count -ne 2 -or
        $inventory.excludes[0] -cne 'inventory.json' -or $inventory.excludes[1] -cne 'checksums.sha256') {
        throw 'Invalid model handover inventory.'
    }
    $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $files = @(Get-ModelHandoverFiles $fullRoot | Where-Object { $_.FullName -cne (Join-Path $fullRoot 'inventory.json') -and $_.FullName -cne (Join-Path $fullRoot 'checksums.sha256') })
    if ($files.Count -ne @($inventory.files).Count) { throw 'Model handover inventory file count mismatch.' }
    foreach ($entry in $inventory.files) {
        Assert-ModelRelativePath $entry.path
        if ($entry.path -in @('inventory.json', 'checksums.sha256') -or -not $seen.Add($entry.path) -or
            $entry.sizeBytes -lt 0 -or $entry.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid model handover inventory entry.' }
        $stamp = Get-ModelFileStamp (Join-Path $fullRoot $entry.path)
        if ($stamp.sizeBytes -ne $entry.sizeBytes -or $stamp.sha256 -cne $entry.sha256) { throw 'Model handover inventory checksum mismatch.' }
    }
    [pscustomobject]@{ inventoryVerified = $true; filesVerified = $files.Count; runtimeProbed = $false }
}

Export-ModuleMember -Function Assert-ModelSafePath, Assert-ModelRelativePath, Get-ModelFileStamp, Copy-ModelVerifiedFile,
    Test-ModelPiperArchive, Write-ModelDistributionJson, Test-ModelSnapshotPath, Test-ModelHandoverInventory
