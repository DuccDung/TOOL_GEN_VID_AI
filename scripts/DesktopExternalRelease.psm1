$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

function Get-DesktopExternalDownloadPolicy {
    # Read the executable-source policy from source, never an old built DLL or runtime settings.
    $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../TOOL-DISTRIBUTION/Updates/DesktopArtifactDownloadPolicy.cs'))
    $prefixes = @()
    foreach ($name in @('ApplicationPrefix', 'ModelHostPackagePrefix')) {
        $match = [regex]::Match($source, ('public const string ' + $name + ' = "([^"]+)";'))
        if (-not $match.Success) { throw 'Cannot read the approved executable download policy from source.' }
        $prefixes += $match.Groups[1].Value
    }
    if ($source -notmatch 'public const int PolicyVersion = 1;' -or
        $source -notmatch 'MaximumAppArchiveBytes = 2L \* 1024 \* 1024 \* 1024;' -or
        $source -notmatch 'MaximumExpandedBytes = 4L \* 1024 \* 1024 \* 1024;' -or
        $source -notmatch 'MaximumEntryCount = 200_000;' -or
        $source -notmatch 'MaximumSetupArtifactBytes = 512L \* 1024 \* 1024;' -or
        $source -notmatch 'MaximumArtifactFileNameLength = 180;') {
        throw 'The executable download policy changed; update the release validator before publishing.'
    }
    [pscustomobject]@{ Version = 1; Prefixes = $prefixes; MaximumArchiveBytes = [long]2147483648; MaximumExpandedBytes = [long]4294967296; MaximumEntries = 200000; MaximumSetupBytes = [long]536870912; MaximumFileNameLength = 180 }
}

function Test-DesktopExternalDownloadUrl([string]$DownloadUrl) {
    $policy = Get-DesktopExternalDownloadPolicy
    if ([string]::IsNullOrWhiteSpace($DownloadUrl) -or $DownloadUrl.Length -gt 2000 -or $DownloadUrl -cne $DownloadUrl.Trim() -or
        $DownloadUrl -match '[\s\x00-\x1f\x7f\\%?#]' -or @($DownloadUrl.Split('/') | Where-Object { $_ -in @('.', '..') }).Count) {
        throw 'The application ZIP URL contains unsupported characters or path segments.'
    }
    $uri = $null
    if (-not [Uri]::TryCreate($DownloadUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -cne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) {
        throw 'The application ZIP requires a public HTTPS URL without credentials, query or fragment.'
    }
    $allowed = $false
    foreach ($prefix in $policy.Prefixes) {
        $root = [Uri]$prefix
        if ($uri.Scheme -ceq $root.Scheme -and $uri.IdnHost -ieq $root.IdnHost -and $uri.Port -eq $root.Port -and
            $uri.AbsolutePath.StartsWith($root.AbsolutePath, [StringComparison]::Ordinal) -and $uri.AbsolutePath.Length -gt $root.AbsolutePath.Length) { $allowed = $true }
    }
    if (-not $allowed) { throw 'The application ZIP URL is outside the compiled approved VPS origins/path prefixes.' }
    $uri.AbsoluteUri
}

function Test-DesktopCustomerApiUrl([string]$ServerBaseUrl) {
    $uri = $null
    if (-not [Uri]::TryCreate($ServerBaseUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -cne 'https' -or
        $uri.IsLoopback -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $ServerBaseUrl -match '[\x00-\x1f\\]') {
        throw 'A customer Setup requires a non-loopback HTTPS VideoMaker API URL, separate from the application ZIP URL.'
    }
    $uri.AbsoluteUri.TrimEnd('/') + '/'
}

function Assert-DesktopRegistrationPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    for ($item = New-Object IO.DirectoryInfo (Split-Path -Parent $full); $null -ne $item; $item = $item.Parent) {
        if ($item.Exists -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Registration paths cannot traverse reparse points.' }
    }
    if ((Test-Path -LiteralPath $full) -and ((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Registration paths cannot be reparse points.' }
    $full
}

function Get-DesktopRegistrationPackage([string]$PackagePath, [string]$Version, [int]$BuildNumber) {
    $policy = Get-DesktopExternalDownloadPolicy
    if ([IO.Path]::GetFileName($PackagePath).Length -gt $policy.MaximumFileNameLength) { throw 'The application ZIP filename exceeds the 180 character release policy.' }
    $path = Assert-DesktopRegistrationPath $PackagePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or [IO.Path]::GetExtension($path) -ine '.zip') { throw 'The application ZIP does not exist.' }
    $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $zip = $null
    try {
        if ($stream.Length -lt 4 -or $stream.Length -gt $policy.MaximumArchiveBytes) { throw 'The application ZIP exceeds the approved archive size policy.' }
        $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
        if ($zip.Entries.Count -lt 1 -or $zip.Entries.Count -gt $policy.MaximumEntries) { throw 'The application ZIP entry count is invalid.' }
        $names = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $files = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        [long]$expanded = 0
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName
            if (-not $name -or $name.StartsWith('/') -or $name.Contains('\') -or -not $names.Add($name) -or
                (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000 -or ($entry.ExternalAttributes -band 0x400)) { throw 'The application ZIP contains an unsafe/duplicate/link entry.' }
            foreach ($segment in $name.TrimEnd('/').Split('/')) {
                if (-not $segment -or $segment -in @('.', '..') -or $segment -match '[:<>"|?*\x00-\x1f]' -or $segment.EndsWith('.') -or
                    $segment.EndsWith(' ') -or $segment -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw 'The application ZIP contains an unsafe entry path.' }
            }
            if ($entry.Length -gt $policy.MaximumExpandedBytes - $expanded) { throw 'The application ZIP exceeds the expanded size policy.' }
            $expanded += $entry.Length
            if (-not $name.EndsWith('/')) { $null = $files.Add($name) }
        }
        $manifestEntry = $zip.GetEntry('update-manifest.json')
        if (-not $manifestEntry -or $manifestEntry.Length -gt 4194304) { throw 'Missing or oversized application update manifest.' }
        $reader = New-Object IO.StreamReader($manifestEntry.Open(), [Text.Encoding]::UTF8)
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($manifest.product -cne 'VideoMaker' -or $manifest.version -cne $Version -or $manifest.buildNumber -ne $BuildNumber -or
            $manifest.platform -cne 'win-x64' -or @($manifest.managedFiles).Count -ne $files.Count) { throw 'Application manifest identity or file inventory does not match the ZIP/release.' }
        $managed = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($name in @($manifest.managedFiles)) {
            if (-not $files.Contains([string]$name) -or -not $managed.Add([string]$name)) { throw 'Application manifest contains missing or duplicate managed files.' }
        }
        foreach ($required in @('TOOL-LOCAL.exe', 'update-manifest.json', 'package-inventory.json')) {
            if (-not $managed.Contains($required)) { throw 'Application manifest is missing a required package file.' }
        }
        [pscustomobject]@{ FileName = [IO.Path]::GetFileName($path); SizeBytes = $stream.Length; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); Manifest = $manifest; ExpandedSizeBytes = $expanded; EntryCount = $zip.Entries.Count }
    } finally { if ($zip) { $zip.Dispose() }; $stream.Dispose() }
}

function Invoke-DesktopSetupBindingProbe([string]$SetupPath) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = Assert-DesktopRegistrationPath $SetupPath
    $start.Arguments = '--inspect-setup-binding'
    $start.WorkingDirectory = Split-Path -Parent $start.FileName
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw 'Cannot inspect the freshly built Setup binding.' }
        $output = $process.StandardOutput.ReadToEndAsync(); $errorOutput = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); $process.WaitForExit(); throw 'Setup binding inspection timed out.' }
        $null = $errorOutput.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw 'The freshly built Setup rejected binding inspection.' }
        $output.GetAwaiter().GetResult() | ConvertFrom-Json
    } finally { $process.Dispose() }
}

function New-DesktopExternalRegistration {
    param([string]$PackagePath, [string]$SetupPath, [string]$Version, [int]$BuildNumber,
        [ValidateSet('Stable', 'Beta', 'Development')][string]$Channel, [string]$ServerBaseUrl, [string]$DownloadUrl,
        [Parameter(Mandatory = $true)]$SetupBinding, [string]$OutputPath,
        [string]$ExpectedPackageSha256 = '', [string]$ExpectedSetupSha256 = '')
    $approvedUrl = Test-DesktopExternalDownloadUrl $DownloadUrl
    $api = Test-DesktopCustomerApiUrl $ServerBaseUrl
    $package = Get-DesktopRegistrationPackage -PackagePath $PackagePath -Version $Version -BuildNumber $BuildNumber
    $setup = Assert-DesktopRegistrationPath $SetupPath
    $policy = Get-DesktopExternalDownloadPolicy
    if (-not (Test-Path -LiteralPath $setup -PathType Leaf) -or [IO.Path]::GetExtension($setup) -ine '.exe' -or (Get-Item -LiteralPath $setup).Length -lt 2 -or
        (Get-Item -LiteralPath $setup).Length -gt $policy.MaximumSetupBytes -or [IO.Path]::GetFileName($setup).Length -gt $policy.MaximumFileNameLength -or
        $package.FileName.Length -gt $policy.MaximumFileNameLength) { throw 'The bound Setup or artifact filename violates the release policy.' }
    if ($SetupBinding.schemaVersion -ne 1 -or $SetupBinding.version -cne $Version -or $SetupBinding.buildNumber -ne $BuildNumber -or
        $SetupBinding.packageSha256 -cne $package.Sha256 -or $SetupBinding.channel -cne $Channel -or $SetupBinding.platform -cne 'win-x64' -or
        $SetupBinding.serverBaseUrl.TrimEnd('/') -cne $api.TrimEnd('/') -or $SetupBinding.installMode -cne 'StaticArchive' -or
        $SetupBinding.distributionProtocol -ne 2 -or $SetupBinding.modelBundleId -cne 'models-20261016-03') { throw 'Actual Setup metadata does not match the application ZIP, API or release identity.' }
    $document = [ordered]@{
        schemaVersion = 1; version = $Version; buildNumber = $BuildNumber; channel = $Channel; platform = 'win-x64'
        downloadUrl = $approvedUrl; fileName = $package.FileName; sizeBytes = $package.SizeBytes; sha256 = $package.Sha256
        packageManifest = $package.Manifest; expandedSizeBytes = $package.ExpandedSizeBytes; entryCount = $package.EntryCount
        setup = [ordered]@{ fileName = [IO.Path]::GetFileName($setup); sizeBytes = (Get-Item -LiteralPath $setup).Length; sha256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant() }
        installMode = 'StaticArchive'; distributionProtocol = 2; modelBundleId = 'models-20261016-03'
    }
    if (($ExpectedPackageSha256 -and $document.sha256 -cne $ExpectedPackageSha256) -or
        ($ExpectedSetupSha256 -and $document.setup.sha256 -cne $ExpectedSetupSha256)) { throw 'Application or Setup bytes changed during release assembly.' }
    $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes(($document | ConvertTo-Json -Depth 10))
    if ($bytes.Length -gt 4 * 1024 * 1024) { throw 'Serialized external registration exceeds the 4 MiB metadata limit.' }
    if ($OutputPath) {
        $destination = Assert-DesktopRegistrationPath $OutputPath
        if (-not (Test-Path -LiteralPath (Split-Path -Parent $destination) -PathType Container)) { throw 'Registration output directory must already exist.' }
        $output = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $output.Write($bytes, 0, $bytes.Length) }
        finally { $output.Dispose() }
    }
    [pscustomobject]$document
}

Export-ModuleMember -Function Get-DesktopExternalDownloadPolicy, Test-DesktopExternalDownloadUrl, Test-DesktopCustomerApiUrl,
    Get-DesktopRegistrationPackage, Invoke-DesktopSetupBindingProbe, New-DesktopExternalRegistration
