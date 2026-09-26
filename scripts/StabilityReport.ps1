# Pure report helpers; loading this file does not start processes or change files.
function Get-StabilityCaseFingerprint([object[]]$Cases) {
    [string[]]$entries = @($Cases | ForEach-Object {
        # VSTest may truncate theory display names. Include its stable test ID and
        # preserve duplicate entries so dropping a data row changes the fingerprint.
        ConvertTo-Json -Compress -InputObject ([ordered]@{ Id = $_.Id; Name = $_.Name; Outcome = $_.Outcome })
    })
    [Array]::Sort($entries, [StringComparer]::Ordinal)
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($entries -join "`n")))).Replace('-', '')
    } finally { $hash.Dispose() }
}
