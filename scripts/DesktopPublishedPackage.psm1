#requires -Version 5.1
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DesktopOfflinePackage.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'DesktopExternalRelease.psm1') -Force

function Assert-DesktopPublishedPath([string]$Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw 'Desktop packaging paths must be absolute.' }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full -eq [IO.Path]::GetPathRoot($full).TrimEnd('\', '/')) { throw 'Desktop packaging cannot use a volume root.' }
    $current = $full
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Desktop packaging paths cannot traverse reparse points.'
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if (-not $parent -or $parent -eq $current) { break }
        $current = $parent
    }
    $full
}

function Assert-DesktopPublishedRelativePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Length -gt 260 -or $Path.StartsWith('/') -or $Path.Contains('\')) {
        throw 'Unsafe desktop publish relative path.'
    }
    foreach ($segment in $Path.Split('/')) {
        if (-not $segment -or $segment -in @('.', '..') -or $segment -match '[:<>"|?*\x00-\x1f]' -or
            $segment.EndsWith('.') -or $segment.EndsWith(' ') -or $segment -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
            throw 'Unsafe desktop publish relative path.'
        }
    }
}

function Assert-DesktopSharedPackageVersion([string]$Version) {
    if (-not $Version -or $Version.Length -gt 32 -or $Version -cnotmatch '\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\z') {
        throw 'Desktop release versions must use canonical major.minor.patch.'
    }
    foreach ($part in $Version.Split('.')) {
        $number = 0
        if (-not [int]::TryParse($part, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
            throw 'Desktop version components must fit Int32.'
        }
    }
}

function Test-DesktopPublishedIdentity {
    param([string]$PublishRoot, [string]$Version, [int]$BuildNumber)
    Assert-DesktopSharedPackageVersion $Version
    if ($Version -ceq '0.0.0' -or $BuildNumber -le 0) { throw 'Desktop releases require a positive version and build number.' }
    $root = Assert-DesktopPublishedPath $PublishRoot
    $executable = Assert-DesktopPublishedPath (Join-Path $root 'TOOL-LOCAL.exe')
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Published desktop executable is missing.' }
    $stream = [IO.File]::OpenRead($executable)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw 'Published desktop is not a Windows executable.' }
        $stream.Position = 0x3c
        $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt $stream.Length - 24) { throw 'Published desktop has an invalid PE header.' }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Published desktop must target Windows x64.' }
    } finally { $reader.Dispose(); $stream.Dispose() }
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
    if ($actual -cne ($Version + '+desktopbuild.' + $BuildNumber)) {
        throw 'Published desktop native version/build does not match the requested release. Publish again with the shared release profile.'
    }
    [pscustomobject]@{ Version = $Version; BuildNumber = $BuildNumber; NativeIdentityVerified = $true }
}

function Get-DesktopPublishedFiles([string]$PublishRoot) {
    $root = Assert-DesktopPublishedPath $PublishRoot
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Desktop publish root does not exist.' }
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    $pending = New-Object 'Collections.Generic.Stack[string]'
    $pending.Push($root)
    $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $count = 0
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Desktop publish must not contain reparse points.' }
            $full = Assert-DesktopPublishedPath $item.FullName
            if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Desktop publish file is outside its root.' }
            $relative = $full.Substring($prefix.Length).Replace('\', '/')
            Assert-DesktopPublishedRelativePath $relative
            if (-not $seen.Add($relative)) { throw 'Desktop publish contains case-colliding paths.' }
            $count++
            if ($count -gt 200000) { throw 'Desktop publish contains too many entries.' }
            if ($item.PSIsContainer) { $pending.Push($full) }
            else { [pscustomobject]@{ Path = $relative; FullName = $full; SizeBytes = [long]$item.Length } }
        }
    }
}

function Assert-DesktopPublishedPayload([string]$RelativePath) {
    # Runtime and model readiness belongs to the target user's machine. No cache,
    # Python environment, model weights, previous ZIP or account data enters an app ZIP.
    if ($RelativePath -match '(?i)(^|/)(?:\.git|\.vs|node_modules|\.venv|venv|__pycache__|[^/]*\.WebView2|cache|caches|components|models|workspace|workspaces|logs)(/|$)' -or
        $RelativePath -match '(?i)(?:\.gguf|\.safetensors|\.onnx|\.pth|\.pt|\.pyc|\.zip|\.part|\.pfx|\.p12|\.pem|\.key|\.snk)$' -or
        $RelativePath -match '(?i)(^|/)(?:\.?ready(?:[.-][^/]*)?|static-setup-receipt\.json|static-model-install(?:\.pending)?\.json|.*\.receipt\.json|\.env(?:\.[^/]*)?|provider-secrets\.bin(?:\.[^/]*)?|secrets\.json|credentials\.json|service-account\.json|preferences\.json|device-id\.bin|auth-token\.bin|session\.bin|tokens\.bin)$' -or
        ($RelativePath -match '(?i)(^|/)appsettings(?:\.[^/]*)?\.json$' -and $RelativePath -cne 'appsettings.json')) {
        throw 'Desktop publish contains private state, cached runtime or a bundled model/archive. Publish to a fresh staging folder.'
    }
}

function Assert-DesktopPublishedFreeSpace {
    param([string]$Path, [long]$RequiredBytes)
    $full = Assert-DesktopPublishedPath $Path
    try { $drive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($full)); $available = $drive.AvailableFreeSpace }
    catch { throw 'Cannot verify free space for desktop package staging.' }
    if ($RequiredBytes -lt 0 -or $available -lt $RequiredBytes) {
        throw 'Insufficient free disk space for verified desktop ZIP staging and extraction. Previous releases were preserved.'
    }
}

function Copy-DesktopPublishedPayload {
    param([string]$PublishRoot, [string]$PackageRoot, [string]$PublishFileListPath = '')
    $sourceRoot = Assert-DesktopPublishedPath $PublishRoot
    $targetRoot = Assert-DesktopPublishedPath $PackageRoot
    if ($targetRoot.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $sourceRoot.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $sourceRoot -ieq $targetRoot) {
        throw 'Desktop package staging and publish roots must be separate.'
    }
    if ((Test-Path -LiteralPath $targetRoot) -and @(Get-ChildItem -LiteralPath $targetRoot -Force).Count) { throw 'Desktop package staging must be empty.' }
    $allFiles = @(Get-DesktopPublishedFiles $sourceRoot)
    $selected = $null
    if ($PublishFileListPath) {
        $fileList = Assert-DesktopPublishedPath $PublishFileListPath
        $selected = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($line in [IO.File]::ReadAllLines($fileList, [Text.Encoding]::UTF8)) {
            $relative = $line.Replace('\', '/')
            Assert-DesktopPublishedRelativePath $relative
            if (-not $selected.Add($relative)) { throw 'Desktop publish file list contains duplicate paths.' }
        }
        if ($selected.Count -eq 0) { throw 'Desktop publish file list is empty.' }
        $available = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in $allFiles) { $null = $available.Add($file.Path) }
        foreach ($path in $selected) { if (-not $available.Contains($path)) { throw 'Desktop publish file list contains a missing file.' } }
    }
    $files = @($allFiles | Where-Object { -not $selected -or $selected.Contains($_.Path) })
    foreach ($file in $files) { Assert-DesktopPublishedPayload $file.Path }
    $files = @($files | Where-Object {
        [IO.Path]::GetExtension($_.Path) -notin @('.pdb', '.xml') -and
        $_.Path -notin @('update-manifest.json', 'distribution-profile.json', 'package-inventory.json', 'VideoMaker.Updater.runtimeconfig.json')
    })
    if (-not $files.Count) { throw 'Desktop publish has no distributable files.' }
    [long]$size = 0
    foreach ($file in $files) {
        $size += $file.SizeBytes
        if ($size -gt 4294967296) { throw 'Desktop publish exceeds the 4 GiB expanded limit.' }
    }
    Assert-DesktopPublishedFreeSpace -Path $targetRoot -RequiredBytes ($size + [long]67108864)
    [IO.Directory]::CreateDirectory($targetRoot) | Out-Null
    foreach ($file in $files) {
        $source = Assert-DesktopPublishedPath $file.FullName
        $target = Assert-DesktopPublishedPath (Join-Path $targetRoot $file.Path)
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -ErrorAction Stop
        # Detect concurrent changes to the Visual Studio output while copying.
        if ((Get-Item -LiteralPath $source -Force).Length -ne $file.SizeBytes -or
            (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
            throw 'Desktop publish changed during packaging. Wait for the current Publish to finish and retry.'
        }
    }
    [pscustomobject]@{ FilesCopied = $files.Count; ExpandedSizeBytes = $size }
}

function New-DesktopSharedPackageMetadata {
    param([string]$PackageRoot, [string]$Version, [int]$BuildNumber, [string]$ServerBaseUrl,
        [string]$ReleaseProfile = 'Production', [string]$MinimumBootstrapperVersion = '1.0.0')
    Assert-DesktopSharedPackageVersion $Version
    Assert-DesktopSharedPackageVersion $MinimumBootstrapperVersion
    $ServerBaseUrl = Test-DesktopCustomerApiUrl $ServerBaseUrl
    if ($BuildNumber -le 0 -or $ReleaseProfile -cnotmatch '\A[A-Za-z0-9][A-Za-z0-9._-]{0,49}\z') { throw 'Invalid desktop package release identity.' }
    $root = Assert-DesktopPublishedPath $PackageRoot
    foreach ($name in @('update-manifest.json', 'distribution-profile.json', 'package-inventory.json')) {
        if (Test-Path -LiteralPath (Join-Path $root $name)) { throw 'Desktop package metadata must be generated only once.' }
    }
    $files = @(Get-DesktopPublishedFiles $root | ForEach-Object { $_.Path })
    [ordered]@{
        schemaVersion = 1; distributionProtocol = 3; profile = $ReleaseProfile
        serverBaseUrl = $ServerBaseUrl.TrimEnd('/') + '/'; channel = 'Stable'; platform = 'win-x64'
        modelBundleId = 'models-20261016-03'; minimumBootstrapperVersion = $MinimumBootstrapperVersion
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'distribution-profile.json') -Encoding UTF8
    [ordered]@{
        product = 'VideoMaker'; version = $Version; buildNumber = $BuildNumber; platform = 'win-x64'
        managedFiles = @(@($files) + @('update-manifest.json', 'distribution-profile.json', 'package-inventory.json') | Sort-Object -Unique)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'update-manifest.json') -Encoding UTF8
    New-DesktopPackageInventory -PackageRoot $root
}

function Get-DesktopPublishedArtifactReport([string]$Root, [string]$Version, [int]$BuildNumber) {
    $expected = @(('VideoMaker-' + $Version + '-' + $BuildNumber + '-win-x64.zip'), 'checksums.sha256', 'package-report.json')
    $actual = @(Get-DesktopPublishedFiles $Root)
    if ($actual.Count -ne 3 -or @($actual | Where-Object { $_.Path -cnotin $expected }).Count) { throw 'Desktop final staging contains unexpected or incomplete release artifacts.' }
    $packagePath = Join-Path $Root $expected[0]
    $report = [IO.File]::ReadAllText((Join-Path $Root 'package-report.json')) | ConvertFrom-Json
    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $packagePath).Length
    if ($report.schemaVersion -ne 1 -or $report.deliveryMode -cne 'SharedVps' -or $report.version -cne $Version -or $report.buildNumber -ne $BuildNumber -or
        $report.package -cne $expected[0] -or $report.sizeBytes -ne $size -or $report.sha256 -cne $hash -or $report.setupBuilt -ne $false -or
        $report.productionQualified -ne $false -or $report.packageVerified -ne $true -or
        ([IO.File]::ReadAllText((Join-Path $Root 'checksums.sha256')).Trim()) -cne ($hash + '  ' + $expected[0])) {
        throw 'Desktop final staging report/checksum does not match its verified ZIP.'
    }
    [pscustomobject]@{ Path = $packagePath; SizeBytes = $size; Sha256 = $hash; Report = $report }
}

function Test-DesktopPublishedPackagePayloadEqual([string]$FirstPath, [string]$SecondPath) {
    $firstStream = $null; $secondStream = $null
    $first = $null; $second = $null
    try {
        $firstStream = [IO.File]::OpenRead($FirstPath)
        $secondStream = [IO.File]::OpenRead($SecondPath)
        $first = New-Object IO.Compression.ZipArchive($firstStream, [IO.Compression.ZipArchiveMode]::Read, $true)
        $second = New-Object IO.Compression.ZipArchive($secondStream, [IO.Compression.ZipArchiveMode]::Read, $true)
        if ($first.Entries.Count -ne $second.Entries.Count -or $first.Entries.Count -gt 200000) { return $false }
        $seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $first.Entries) {
            Assert-DesktopPublishedRelativePath $entry.FullName
            if (-not $seen.Add($entry.FullName) -or $entry.FullName.EndsWith('/')) { return $false }
            $other = $second.GetEntry($entry.FullName)
            if (-not $other -or $entry.Length -ne $other.Length) { return $false }
            $input = $null; $otherInput = $null; $sha = $null; $otherSha = $null
            try {
                $input = $entry.Open(); $otherInput = $other.Open()
                $sha = [Security.Cryptography.SHA256]::Create(); $otherSha = [Security.Cryptography.SHA256]::Create()
                $firstHash = [Convert]::ToBase64String($sha.ComputeHash($input))
                $otherHash = [Convert]::ToBase64String($otherSha.ComputeHash($otherInput))
                if ($firstHash -cne $otherHash) { return $false }
            } finally { if ($input) { $input.Dispose() }; if ($otherInput) { $otherInput.Dispose() }; if ($sha) { $sha.Dispose() }; if ($otherSha) { $otherSha.Dispose() } }
        }
        # Each entry in the new, already verified ZIP found the exact name in the
        # existing ZIP. Reject duplicate/case-colliding or link entries as well.
        $otherSeen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $second.Entries) {
            if (-not $otherSeen.Add($entry.FullName) -or -not $seen.Contains($entry.FullName) -or
                (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000 -or ($entry.ExternalAttributes -band 0x400)) { return $false }
        }
        return $true
    } finally { if ($first) { $first.Dispose() }; if ($second) { $second.Dispose() }; if ($firstStream) { $firstStream.Dispose() }; if ($secondStream) { $secondStream.Dispose() } }
}

function Complete-DesktopPublishedPackage {
    param([string]$StagingRoot, [string]$OutputRoot, [string]$Version, [int]$BuildNumber)
    Assert-DesktopSharedPackageVersion $Version
    if ($BuildNumber -le 0) { throw 'Desktop build number must be positive.' }
    $staging = Assert-DesktopPublishedPath $StagingRoot
    $output = Assert-DesktopPublishedPath $OutputRoot
    $parent = [IO.Path]::GetDirectoryName($staging)
    if ($parent -ine $output -or [IO.Path]::GetFileName($staging) -cnotmatch '^\.vm-package-[0-9a-f]{32}\.part$') {
        throw 'Desktop package promotion requires an owned staging directory in its output root.'
    }
    $finalRoot = Assert-DesktopPublishedPath (Join-Path $output ($Version + '-' + $BuildNumber))
    $artifact = Get-DesktopPublishedArtifactReport -Root $staging -Version $Version -BuildNumber $BuildNumber
    $reused = $false
    if (Test-Path -LiteralPath $finalRoot) {
        $existing = Get-DesktopPublishedArtifactReport -Root $finalRoot -Version $Version -BuildNumber $BuildNumber
        if (-not (Test-DesktopPublishedPackagePayloadEqual -FirstPath $artifact.Path -SecondPath $existing.Path)) {
            throw 'This version/build already has different immutable release output. Increase the build number; the previous ZIP was preserved.'
        }
        $artifact = $existing
        $reused = $true
    } else {
        # Directory.Move cannot overwrite a previous release and is atomic on this volume.
        [IO.Directory]::Move($staging, $finalRoot)
    }
    [pscustomobject]@{ Version = $Version; BuildNumber = $BuildNumber; Package = (Join-Path $finalRoot ([IO.Path]::GetFileName($artifact.Path)));
        SizeBytes = $artifact.SizeBytes; Sha256 = $artifact.Sha256; DeliveryMode = 'SharedVps'; SetupBuilt = $false; ProductionQualified = $false; ReusedExisting = $reused }
}

function Remove-DesktopOwnedPackagingDirectory {
    param([string]$Path, [string]$Parent, [string]$NamePattern)
    $full = Assert-DesktopPublishedPath $Path
    $parentFull = Assert-DesktopPublishedPath $Parent
    if ([IO.Path]::GetDirectoryName($full) -ine $parentFull -or [IO.Path]::GetFileName($full) -cnotmatch $NamePattern) {
        throw 'Refusing cleanup of an unowned desktop packaging directory.'
    }
    if (Test-Path -LiteralPath $full) {
        $null = @(Get-DesktopPublishedFiles $full)
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}

function Remove-DesktopVerifiedPackagePayload {
    param([string]$PayloadRoot, [string]$OutputRoot, [string]$ArchivePath, [string]$Version,
        [int]$BuildNumber, [string]$ExpectedSha256)
    # This is a packaging staging directory, never the user's publish output.
    # Tie the payload and archive to the same run before reading or deleting it.
    $payload = Assert-DesktopPublishedPath $PayloadRoot
    $output = Assert-DesktopPublishedPath $OutputRoot
    $archivePathSafe = Assert-DesktopPublishedPath $ArchivePath
    $ownedName = [regex]::Match([IO.Path]::GetFileName($payload), '\A\.vm-package-payload-([0-9a-f]{32})\.part\z')
    if ([IO.Path]::GetDirectoryName($payload) -ine $output -or -not $ownedName.Success) {
        throw 'Refusing release of an unowned desktop package payload.'
    }
    $runId = $ownedName.Groups[1].Value
    $expectedArchiveRoot = Join-Path $output ('.vm-package-' + $runId + '.part')
    if ([IO.Path]::GetDirectoryName($archivePathSafe) -ine $expectedArchiveRoot -or
        [IO.Path]::GetFileName($archivePathSafe) -cne ('VideoMaker-' + $Version + '-' + $BuildNumber + '-win-x64.zip')) {
        throw 'The verified archive must belong to the same desktop packaging run.'
    }
    if ($ExpectedSha256 -cnotmatch '\A[0-9a-f]{64}\z') { throw 'Payload release requires the expected lowercase ZIP SHA-256.' }
    Assert-DesktopSharedPackageVersion $Version
    if ($BuildNumber -le 0) { throw 'Payload release requires a positive build number.' }
    $files = @(Get-DesktopPublishedFiles $payload)
    $null = Test-DesktopPackageInventory -PackageRoot $payload
    $null = Test-DesktopPublishedIdentity -PublishRoot $payload -Version $Version -BuildNumber $BuildNumber
    $inventory = Get-Content -LiteralPath (Join-Path $payload 'package-inventory.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $expected = @{}
    foreach ($file in @($inventory.files)) { $expected[$file.path] = $file }
    $inventoryPath = Join-Path $payload 'package-inventory.json'
    $expected['package-inventory.json'] = [pscustomobject]@{
        path = 'package-inventory.json'; sizeBytes = (Get-Item -LiteralPath $inventoryPath).Length
        sha256 = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $stream = [IO.File]::Open($archivePathSafe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $archive = $null
    try {
        $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
        try { $hash = ([BitConverter]::ToString($hashAlgorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
        finally { $hashAlgorithm.Dispose() }
        if ($hash -cne $ExpectedSha256) { throw 'The application ZIP changed before releasing its staging payload.' }
        $registration = Get-DesktopRegistrationPackage -PackagePath $archivePathSafe -Version $Version -BuildNumber $BuildNumber
        if ($registration.Sha256 -cne $ExpectedSha256) { throw 'The verified ZIP does not match the expected release hash.' }
        $stream.Position = 0
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
        if ($archive.Entries.Count -ne $files.Count) { throw 'The application ZIP does not exactly match its staging payload.' }
        foreach ($entry in $archive.Entries) {
            if (-not $expected.ContainsKey($entry.FullName) -or $entry.Length -ne $expected[$entry.FullName].sizeBytes) {
                throw 'The application ZIP inventory does not match its staging payload.'
            }
            $input = $null; $entryHash = $null
            try {
                $input = $entry.Open()
                $entryHash = [Security.Cryptography.SHA256]::Create()
                $actual = ([BitConverter]::ToString($entryHash.ComputeHash($input))).Replace('-', '').ToLowerInvariant()
                if ($actual -cne $expected[$entry.FullName].sha256) { throw 'The application ZIP entry SHA-256 does not match its staging payload.' }
            } finally { if ($input) { $input.Dispose() }; if ($entryHash) { $entryHash.Dispose() } }
        }
        # Only the exact owned payload can be removed. Keep the verified ZIP read
        # handle pinned until removal finishes, then extract and probe the ZIP.
        Remove-DesktopOwnedPackagingDirectory -Path $payload -Parent $output -NamePattern '^\.vm-package-payload-[0-9a-f]{32}\.part$'
        [pscustomobject]@{ PayloadReleased = $true; ZipVerified = $true; Sha256 = $hash; FilesVerified = $files.Count }
    } finally { if ($archive) { $archive.Dispose() }; $stream.Dispose() }
}

Export-ModuleMember -Function Assert-DesktopPublishedPath, Assert-DesktopSharedPackageVersion, Test-DesktopPublishedIdentity,
    Assert-DesktopPublishedRelativePath, Assert-DesktopPublishedPayload,
    Get-DesktopPublishedFiles, Copy-DesktopPublishedPayload, New-DesktopSharedPackageMetadata, Complete-DesktopPublishedPackage,
    Remove-DesktopOwnedPackagingDirectory, Assert-DesktopPublishedFreeSpace, Remove-DesktopVerifiedPackagePayload
