#requires -Version 5.1
Set-StrictMode -Version 2

function Get-StabilityDiskAssessment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('RegressionPair', 'RegressionBridge', 'RegressionCloud', 'Full', 'Frontend', 'Native', 'Timeline', 'Piper', 'SetupOffline', 'StaticSetup')]
        [string]$Profile,
        [Parameter(Mandatory = $true)][ValidateRange(0, [long]::MaxValue)][long]$AvailableBytes
    )
    $minimum = 3L * 1024 * 1024 * 1024
    # Match the two-hour segmented timeline fixture and renderer gate:
    # (7,200 + 120) seconds * 48,000 samples * 2 bytes * 2 channels * 3 files + 512 MiB.
    # Keep the harness floor above that real renderer gate, with extra space for evidence.
    $timelineBytes = (7200L + 120L) * 48000L * 2 * 2 * 3 + 512L * 1024 * 1024
    if ($Profile -in @('Full', 'Timeline')) { $minimum = [Math]::Max($minimum, $timelineBytes + 512L * 1024 * 1024) }
    [pscustomobject]@{ profile = $Profile; availableBytes = $AvailableBytes; requiredBytes = $minimum
        assessment = $(if ($AvailableBytes -ge $minimum) { 'ReadyForHarness' } else { 'Blocked' })
        includesModelInstallationBudget = $false; includesCleanWindowsAcceptance = $false }
}

Export-ModuleMember -Function Get-StabilityDiskAssessment
