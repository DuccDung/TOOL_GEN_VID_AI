Set-StrictMode -Version Latest

function Test-ServerSensitiveSettingPath {
    param([string]$Path)
    return $Path -match '(?i)(^ConnectionStrings\.|Password|SigningKey|Secret|ApiKey|AccessKey|Credential|Token|^Smtp\.User$|^Admin\.BootstrapEmail$|ReceiverAccount(Number|Name)$)'
}

function Clear-ServerSettingNode {
    param([object]$Node, [string]$ParentPath = '')
    if ($null -eq $Node) { return }
    if ($Node -is [pscustomobject]) {
        foreach ($property in $Node.PSObject.Properties) {
            $path = if ($ParentPath) { $ParentPath + '.' + $property.Name } else { $property.Name }
            if ($property.Value -is [string]) {
                if ((Test-ServerSensitiveSettingPath $path) -or $property.Value -match '-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----') { $property.Value = '' }
            } else { Clear-ServerSettingNode -Node $property.Value -ParentPath $path }
        }
    } elseif ($Node -is [array]) {
        for ($index = 0; $index -lt $Node.Length; $index++) {
            if ($Node[$index] -is [string]) {
                if ((Test-ServerSensitiveSettingPath $ParentPath) -or $Node[$index] -match '-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----') { $Node[$index] = '' }
            } else { Clear-ServerSettingNode -Node $Node[$index] -ParentPath $ParentPath }
        }
    }
}

function Set-ServerPackageSafeSettings {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PublishDirectory)
    $path = Join-Path $PublishDirectory 'appsettings.json'
    try {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'missing' }
        $ancestor = Get-Item -LiteralPath $path -Force
        while ($null -ne $ancestor) {
            if (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'reparse' }
            if ($ancestor -is [IO.FileInfo]) { $ancestor = $ancestor.Directory } else { $ancestor = $ancestor.Parent }
        }
        $settings = [IO.File]::ReadAllText($path) | ConvertFrom-Json -ErrorAction Stop
        if ($settings -isnot [pscustomobject]) { throw 'shape' }
        Clear-ServerSettingNode -Node $settings
        if ($null -eq $settings.PSObject.Properties['ModelDistribution']) {
            $settings | Add-Member -NotePropertyName ModelDistribution -NotePropertyValue ([pscustomobject]@{})
        }
        if ($settings.ModelDistribution -isnot [pscustomobject]) { throw 'shape' }
        $settings.ModelDistribution | Add-Member -NotePropertyName Enabled -NotePropertyValue $false -Force
        # Trust is configured explicitly on the target, never inherited from a build workstation.
        $settings.ModelDistribution | Add-Member -NotePropertyName PublicKeysPem -NotePropertyValue ([pscustomobject]@{}) -Force
        $settings.ModelDistribution | Add-Member -NotePropertyName TrustedCatalogSha256ByChannel -NotePropertyValue ([pscustomobject]@{}) -Force
        $settings.ModelDistribution | Add-Member -NotePropertyName StorageRoot -NotePropertyValue 'App_ModelDistribution' -Force
        [IO.File]::WriteAllText($path, ($settings | ConvertTo-Json -Depth 100), (New-Object Text.UTF8Encoding($false)))
    } catch { throw 'Server package root settings could not be sanitized; no settings values are included in this error.' }
}

function Assert-ServerPackageSafe {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PublishDirectory)
    $root = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Server package directory is missing.' }
    $ancestor = Get-Item -LiteralPath $root -Force
    while ($null -ne $ancestor) {
        if (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Server package path contains a reparse point.' }
        $ancestor = $ancestor.Parent
    }
    $pending = New-Object 'Collections.Generic.Stack[string]'
    $pending.Push($root)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        $item = Get-Item -LiteralPath $directory -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Server package contains a reparse point.' }
        foreach ($entry in Get-ChildItem -LiteralPath $directory -Force) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Server package contains a reparse point.' }
            if ($entry.PSIsContainer) {
                if ($entry.Name -match '^(?i:artifacts|output|\.tmp|\.vs|\.git|App_Data|App_Releases|App_ModelDistribution|logs|model-store|DataProtection-Keys|UserSecrets|secrets)$') {
                    throw 'Server package contains a forbidden source-output, runtime-data or secret directory.'
                }
                $pending.Push($entry.FullName)
                continue
            }
            $isRootSettings = $entry.FullName.Equals((Join-Path $root 'appsettings.json'), [StringComparison]::OrdinalIgnoreCase)
            if (($entry.Name -match '(?i)^appsettings.*\.json$' -and -not $isRootSettings) -or
                $entry.Name -match '(?i)(devsettings|development.*\.config$|^secrets?\.|\.user$|\.suo$|\.pfx$|\.p12$|\.pem$|\.key$|\.log$|^\.env(?:\.|$)|^key-.*\.xml$|^launchSettings\.json$)') {
                throw 'Server package contains a forbidden settings or secret file.'
            }
            # Config files are small; never load binary/runtime/model payloads into memory.
            if ($entry.Extension -match '^(?i:\.json|\.config|\.xml|\.txt|\.ini|\.yaml|\.yml|\.env)$') {
                if ($entry.Length -gt 8MB) { throw 'Server package text configuration exceeds the safety scan limit.' }
                $text = [IO.File]::ReadAllText($entry.FullName)
                if ($text -match '-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----') { throw 'Server package contains private key material.' }
                if ($entry.Extension -eq '.config' -and $text -match '(?is)<(?:connectionStrings|environmentVariables)\b') {
                    throw 'Server package config contains environment or connection-string overrides.'
                }
            }
        }
    }
    $path = Join-Path $root 'appsettings.json'
    try {
        $settings = [IO.File]::ReadAllText($path) | ConvertFrom-Json -ErrorAction Stop
        $original = $settings | ConvertTo-Json -Depth 100 -Compress
        Clear-ServerSettingNode -Node $settings
        if ($original -cne ($settings | ConvertTo-Json -Depth 100 -Compress)) { throw 'sensitive' }
        if ($settings.ModelDistribution.Enabled -isnot [bool] -or $settings.ModelDistribution.Enabled -ne $false) { throw 'enabled' }
        if (@($settings.ModelDistribution.PublicKeysPem.PSObject.Properties).Count -ne 0 -or
            @($settings.ModelDistribution.TrustedCatalogSha256ByChannel.PSObject.Properties).Count -ne 0) { throw 'trust' }
    } catch { throw 'Server package root settings failed safety validation; no settings values are included in this error.' }
}

Export-ModuleMember -Function Set-ServerPackageSafeSettings, Assert-ServerPackageSafe
