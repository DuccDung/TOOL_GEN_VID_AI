$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

function Assert-PackageSafePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $item = New-Object IO.DirectoryInfo (Split-Path -Parent $full)
    while ($null -ne $item) {
        if ($item.Exists -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Package paths cannot traverse a reparse point.'
        }
        $item = $item.Parent
    }
    if ((Test-Path -LiteralPath $full) -and ((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Package paths cannot be reparse points.'
    }
    $full
}

function Get-PackageRelativePath([string]$Root, [string]$Path) {
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Package file is outside the package root.'
    }
    $full.Substring($prefix.Length).Replace('\', '/')
}

function Assert-PackageEntryName([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.StartsWith('/') -or $Name.Contains('\')) {
        throw 'Unsafe package entry path.'
    }
    foreach ($segment in $Name.TrimEnd('/').Split('/')) {
        if (-not $segment -or $segment -in @('.', '..') -or $segment -match '[:<>"|?*\x00-\x1f]' -or
            $segment.EndsWith('.') -or $segment.EndsWith(' ') -or
            $segment -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
            throw 'Unsafe package entry path.'
        }
    }
}

function Get-DesktopQwenDefinition {
    $definition = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../third_party/translation/COMPONENT_MANIFEST.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    # Fail closed if a metadata-only edit changes the model approved by the C# runtime.
    if ($definition.componentId -cne 'qwen3-4b-q4-k-m-cpu' -or
        $definition.engineVersion -cne 'bc64014-llamasharp-0.27.0-adapter-1' -or
        $definition.model.fileName -cne 'Qwen3-4B-Q4_K_M.gguf' -or
        $definition.model.sizeBytes -ne 2497280256 -or
        $definition.model.sha256 -cne '7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5') {
        throw 'Qwen metadata does not match the model approved by the desktop runtime.'
    }
    [pscustomobject]@{
        RelativePath = 'third_party/translation/runtime-test/' + $definition.componentId + '/' + $definition.engineVersion + '/' + $definition.model.fileName
        SizeBytes = [long]$definition.model.sizeBytes
        Sha256 = [string]$definition.model.sha256
        EngineVersion = [string]$definition.engineVersion
    }
}

function Test-DesktopQwenModel([string]$ModelPath) {
    $approved = Get-DesktopQwenDefinition
    $resolved = Assert-PackageSafePath $ModelPath
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf) -or
        (Get-Item -LiteralPath $resolved).Length -ne $approved.SizeBytes -or
        (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash -ne $approved.Sha256) {
        throw 'Qwen offline model is missing or does not match the approved size/SHA-256.'
    }
    $approved
}

function Add-DesktopQwenModel([string]$ModelPath, [string]$PackageRoot) {
    $approved = Test-DesktopQwenModel $ModelPath
    $target = Assert-PackageSafePath (Join-Path $PackageRoot $approved.RelativePath)
    if (Test-Path -LiteralPath $target) { throw 'Qwen model target already exists.' }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    Copy-Item -LiteralPath $ModelPath -Destination $target
    Test-DesktopQwenModel $target
}

function Get-DesktopPackageFiles([string]$PackageRoot) {
    $root = Assert-PackageSafePath $PackageRoot
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Package root does not exist.' }
    $entries = @(Get-ChildItem -LiteralPath $root -Recurse -Force)
    if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        throw 'Package must not contain reparse points.'
    }
    foreach ($file in @($entries | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)) {
        $relative = Get-PackageRelativePath $root $file.FullName
        Assert-PackageEntryName $relative
        [pscustomobject]@{ Path = $relative; FullName = $file.FullName; SizeBytes = [long]$file.Length }
    }
}

function New-DesktopPackageInventory([string]$PackageRoot) {
    $inventoryPath = Join-Path $PackageRoot 'package-inventory.json'
    if (Test-Path -LiteralPath $inventoryPath) { throw 'Package inventory already exists.' }
    $files = @(Get-DesktopPackageFiles $PackageRoot | ForEach-Object {
        [ordered]@{ path = $_.Path; sizeBytes = $_.SizeBytes; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    [ordered]@{ schemaVersion = 1; excludes = @('package-inventory.json'); files = $files } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $inventoryPath -Encoding UTF8
}

function Test-DesktopPackageInventory([string]$PackageRoot) {
    $inventory = Get-Content -LiteralPath (Join-Path $PackageRoot 'package-inventory.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($inventory.schemaVersion -ne 1 -or @($inventory.excludes).Count -ne 1 -or $inventory.excludes[0] -cne 'package-inventory.json') {
        throw 'Invalid package inventory.'
    }
    $actual = @(Get-DesktopPackageFiles $PackageRoot | Where-Object { $_.Path -cne 'package-inventory.json' })
    $expected = @($inventory.files)
    if ($actual.Count -ne $expected.Count) { throw 'Package inventory file count mismatch.' }
    $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $expected) {
        Assert-PackageEntryName $entry.path
        if (-not $seen.Add($entry.path)) { throw 'Duplicate package inventory entry.' }
        $target = Assert-PackageSafePath (Join-Path $PackageRoot $entry.path)
        if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or
            (Get-Item -LiteralPath $target).Length -ne $entry.sizeBytes -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw 'Package inventory size/SHA-256 mismatch.'
        }
    }
    [pscustomobject]@{ InventoryVerified = $true; FilesVerified = $actual.Count }
}

function New-DesktopPackageZip([string]$PackageRoot, [string]$DestinationPath) {
    $files = @(Get-DesktopPackageFiles $PackageRoot)
    if (-not $files.Count) { throw 'Package root is empty.' }
    if ($files.Count -gt 200000 -or ($files | Measure-Object SizeBytes -Sum).Sum -gt 4294967296) {
        throw 'Package exceeds the current updater extraction limits (4 GiB / 200000 entries).'
    }
    $destination = Assert-PackageSafePath $DestinationPath
    $prefix = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($destination.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'ZIP output must be outside the package root.' }
    $stream = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $zip = $null
    try {
        $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        foreach ($file in $files) {
            $entry = $zip.CreateEntry($file.Path, [IO.Compression.CompressionLevel]::Optimal)
            $source = [IO.File]::OpenRead($file.FullName)
            $output = $null
            try { $output = $entry.Open(); $source.CopyTo($output, 1048576) }
            finally { if ($output) { $output.Dispose() }; $source.Dispose() }
        }
    } finally { if ($zip) { $zip.Dispose() }; $stream.Dispose() }
}

function Expand-DesktopPackageZip([string]$ZipPath, [string]$DestinationRoot) {
    $root = Assert-PackageSafePath $DestinationRoot
    if ((Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Force).Count) {
        throw 'ZIP extraction requires a new, empty directory.'
    }
    $zipPathSafe = Assert-PackageSafePath $ZipPath
    $stream = [IO.File]::OpenRead($zipPathSafe)
    $zip = $null
    try {
        $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
        $names = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        [long]$total = 0
        if ($zip.Entries.Count -gt 200000) { throw 'Too many package entries.' }
        # Validate all names/limits before writing anything.
        foreach ($entry in $zip.Entries) {
            Assert-PackageEntryName $entry.FullName
            if (-not $names.Add($entry.FullName.TrimEnd('/'))) { throw 'Duplicate package ZIP entry.' }
            if ($entry.Length -lt 0 -or $entry.Length -gt 4294967296 -or
                $total -gt 4294967296 - $entry.Length) { throw 'Package exceeds the 4 GiB extraction limit.' }
            $total += $entry.Length
        }
        [IO.Directory]::CreateDirectory($root) | Out-Null
        foreach ($entry in $zip.Entries) {
            $target = Assert-PackageSafePath (Join-Path $root $entry.FullName)
            $null = Get-PackageRelativePath $root $target
            if ($entry.FullName.EndsWith('/')) { [IO.Directory]::CreateDirectory($target) | Out-Null; continue }
            [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
            $inputStream = $entry.Open()
            $output = $null
            try {
                $output = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                $buffer = New-Object byte[] 1048576
                [long]$written = 0
                while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $written += $read
                    if ($written -gt $entry.Length) { throw 'Extracted package entry exceeds its declared size.' }
                    $output.Write($buffer, 0, $read)
                }
                if ($output.Length -ne $entry.Length) { throw 'Extracted package entry size mismatch.' }
            } finally { if ($output) { $output.Dispose() }; $inputStream.Dispose() }
        }
        [pscustomobject]@{ Entries = $zip.Entries.Count; ExtractedBytes = $total }
    } finally { if ($zip) { $zip.Dispose() }; $stream.Dispose() }
}

Export-ModuleMember -Function Get-DesktopQwenDefinition, Test-DesktopQwenModel, Add-DesktopQwenModel,
    New-DesktopPackageInventory, Test-DesktopPackageInventory, New-DesktopPackageZip, Expand-DesktopPackageZip
