#requires -Version 5.1
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DesktopPublishedPackage.psm1') -Force -DisableNameChecking
Add-Type -AssemblyName System.IO.Compression

function Get-CanonicalStreamHash([IO.Stream]$Stream) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($algorithm.ComputeHash($Stream))).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function New-DesktopCanonicalPackage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PublishRoot,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][ValidateRange(1, 2147483647)][int]$BuildNumber,
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [string]$PublishFileListPath = ''
    )
    Assert-DesktopSharedPackageVersion $Version
    $source = Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($PublishRoot))
    $output = Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($OutputRoot))
    if ($source -ieq $output -or $output.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $source.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Canonical ZIP output and Publish directories must be separate.'
    }
    $allFiles = @(Get-DesktopPublishedFiles $source)
    $selected = $null
    if ($PublishFileListPath) {
        $listPath = Assert-DesktopPublishedPath ([IO.Path]::GetFullPath($PublishFileListPath))
        $selected = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($line in [IO.File]::ReadAllLines($listPath, [Text.Encoding]::UTF8)) {
            $path = $line.Replace('\', '/')
            Assert-DesktopPublishedRelativePath $path
            if (-not $selected.Add($path)) { throw 'Publish file list contains duplicate paths.' }
        }
        if ($selected.Count -eq 0) { throw 'Publish file list is empty.' }
        $available = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in $allFiles) { $null = $available.Add($file.Path) }
        foreach ($path in $selected) { if (-not $available.Contains($path)) { throw 'Publish file list references a missing file.' } }
    }
    $files = @($allFiles | Where-Object { -not $selected -or $selected.Contains($_.Path) })
    foreach ($file in $files) { Assert-DesktopPublishedPayload $file.Path }
    $files = @($files | Where-Object {
        [IO.Path]::GetExtension($_.Path) -notin @('.pdb', '.xml') -and
        $_.Path -notin @('update-manifest.json', 'distribution-profile.json', 'package-inventory.json', 'VideoMaker.Updater.runtimeconfig.json')
    })
    $paths = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $files) { $null = $paths.Add($file.Path) }
    if (-not $paths.Contains('TOOL-LOCAL.exe') -or -not $paths.Contains('appsettings.json') -or $files.Count -gt 200000) {
        throw 'Publish output is missing required application files or exceeds the entry limit.'
    }
    Test-DesktopPublishedIdentity -PublishRoot $source -Version $Version -BuildNumber $BuildNumber | Out-Null
    [long]$expanded = 0
    $byPath = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($file in $files) {
        $expanded += $file.SizeBytes
        if ($expanded -gt 4294967296 -or $file.SizeBytes -lt 0 -or
            ($file.Path -in @('TOOL-LOCAL.exe', 'appsettings.json') -and $file.SizeBytes -eq 0)) { throw 'Publish payload size is invalid.' }
        $stream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            if ($stream.Length -ne $file.SizeBytes) { throw 'Publish output changed before packaging.' }
            $byPath.Add($file.Path, [pscustomobject]@{ Path = $file.Path; FullName = $file.FullName; SizeBytes = $file.SizeBytes; Sha256 = Get-CanonicalStreamHash $stream })
        } finally { $stream.Dispose() }
    }
    $ordered = @($byPath.Keys)
    [Array]::Sort($ordered, [StringComparer]::Ordinal)
    Assert-DesktopPublishedFreeSpace -Path $output -RequiredBytes ($expanded + [long]134217728)
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $temporary = Join-Path $output ('.vm-canonical-' + [Guid]::NewGuid().ToString('N') + '.zip.part')
    try {
        $destination = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try {
            $archive = New-Object IO.Compression.ZipArchive($destination, [IO.Compression.ZipArchiveMode]::Create, $true)
            try {
                $buffer = New-Object byte[] 81920
                foreach ($path in $ordered) {
                    $file = $byPath[$path]
                    Assert-DesktopPublishedPath $file.FullName | Out-Null
                    $input = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
                    try {
                        if ($input.Length -ne $file.SizeBytes) { throw 'Publish output changed during packaging.' }
                        # Explicit canonical entry names avoid the Windows PowerShell 5.1
                        # CreateFromDirectory behavior that writes backslash ZIP paths.
                        $entry = $archive.CreateEntry($path, [IO.Compression.CompressionLevel]::Optimal)
                        $entry.LastWriteTime = New-Object DateTimeOffset(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                        $entryOutput = $entry.Open()
                        $sha = [Security.Cryptography.SHA256]::Create()
                        try {
                            [long]$copied = 0
                            while (($count = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
                                $copied += $count
                                if ($copied -gt $file.SizeBytes) { throw 'Publish output changed during packaging.' }
                                $null = $sha.TransformBlock($buffer, 0, $count, $buffer, 0)
                                $entryOutput.Write($buffer, 0, $count)
                            }
                            $null = $sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
                            $actual = ([BitConverter]::ToString($sha.Hash)).Replace('-', '').ToLowerInvariant()
                            if ($copied -ne $file.SizeBytes -or $actual -cne $file.Sha256) { throw 'Publish file hash changed during packaging.' }
                        } finally { $sha.Dispose(); $entryOutput.Dispose() }
                    } finally { $input.Dispose() }
                    if ($destination.Position -gt 2147483648) { throw 'Application ZIP exceeds the 2 GiB limit.' }
                }
            } finally { $archive.Dispose() }
            $destination.Flush()
            if ($destination.Length -lt 4 -or $destination.Length -gt 2147483648) { throw 'Application ZIP size is invalid.' }
            $destination.Position = 0
            $verification = New-Object IO.Compression.ZipArchive($destination, [IO.Compression.ZipArchiveMode]::Read, $true)
            try {
                if ($verification.Entries.Count -ne $ordered.Count) { throw 'ZIP does not contain the complete Publish payload.' }
                foreach ($entry in $verification.Entries) {
                    if (-not $byPath.ContainsKey($entry.FullName) -or $entry.Length -ne $byPath[$entry.FullName].SizeBytes) { throw 'ZIP payload inventory does not match Publish output.' }
                    $input = $entry.Open()
                    try { if ((Get-CanonicalStreamHash $input) -cne $byPath[$entry.FullName].Sha256) { throw 'ZIP payload hash does not match Publish output.' } }
                    finally { $input.Dispose() }
                }
            } finally { $verification.Dispose() }
            Test-DesktopPublishedIdentity -PublishRoot $source -Version $Version -BuildNumber $BuildNumber | Out-Null
            foreach ($path in $ordered) {
                $file = $byPath[$path]
                Assert-DesktopPublishedPath $file.FullName | Out-Null
                $input = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
                try {
                    if ($input.Length -ne $file.SizeBytes -or (Get-CanonicalStreamHash $input) -cne $file.Sha256) { throw 'Publish output changed before ZIP promotion.' }
                } finally { $input.Dispose() }
            }
            $destination.Position = 0
            $hash = Get-CanonicalStreamHash $destination
            $size = $destination.Length
        } finally { $destination.Dispose() }
        $name = 'VideoMaker-' + $Version + '-' + $BuildNumber + '-win-x64-' + $hash + '.zip'
        if ($name.Length -gt 180) { throw 'Canonical package filename exceeds the release policy limit.' }
        $releaseRoot = Assert-DesktopPublishedPath (Join-Path $output ($Version + '-' + $BuildNumber))
        [IO.Directory]::CreateDirectory($releaseRoot) | Out-Null
        $final = Assert-DesktopPublishedPath (Join-Path $releaseRoot $name)
        $reused = $false
        if ([IO.File]::Exists($final)) {
            $existing = [IO.File]::Open($final, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            try { if ($existing.Length -ne $size -or (Get-CanonicalStreamHash $existing) -cne $hash) { throw 'Existing canonical ZIP does not match its immutable filename.' } }
            finally { $existing.Dispose() }
            $reused = $true
        } else { [IO.File]::Move($temporary, $final) }
        [pscustomobject]@{ PackagePath = $final; FileName = $name; Version = $Version; BuildNumber = $BuildNumber;
            SizeBytes = $size; Sha256 = $hash; ExpandedSizeBytes = $expanded; FilesVerified = $ordered.Count; Reused = $reused;
            ServerInspected = $false; InstalledModelsProbed = $false; ProductionQualified = $false }
    } finally {
        # Only the exact uniquely named file allocated by this invocation is owned.
        if ([IO.File]::Exists($temporary)) {
            Assert-DesktopPublishedPath $temporary | Out-Null
            [IO.File]::Delete($temporary)
        }
    }
}

Export-ModuleMember -Function New-DesktopCanonicalPackage
