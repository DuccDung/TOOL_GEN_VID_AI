#requires -Version 5.1
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'StabilityReport.ps1')
$cases = @(
    [pscustomobject]@{ Id = 'first'; Name = 'UsageParser(DurationSeconds)'; Outcome = 'Passed' },
    [pscustomobject]@{ Id = 'second'; Name = 'UsageParser(durationSeconds)'; Outcome = 'Passed' },
    [pscustomobject]@{ Id = 'third'; Name = 'Truncated theory (...)'; Outcome = 'Passed' },
    [pscustomobject]@{ Id = 'fourth'; Name = 'Truncated theory (...)'; Outcome = 'Passed' }
)
$expected = Get-StabilityCaseFingerprint $cases
$checks = @()
$reversed = @($cases[3], $cases[2], $cases[1], $cases[0])
if ((Get-StabilityCaseFingerprint $reversed) -ne $expected) { throw 'Reordering/case-sensitive names changed the fingerprint.' }
$checks += 'order_independent_with_case_variants'
if ((Get-StabilityCaseFingerprint $cases[0..2]) -eq $expected) { throw 'Missing theory row was not detected.' }
$checks += 'missing_row_detected'
if ((Get-StabilityCaseFingerprint @($cases[0], $cases[1], $cases[2], $cases[2])) -eq $expected) { throw 'Duplicated ID replacing another row was not detected.' }
$checks += 'truncated_names_use_distinct_ids'
if ((Get-StabilityCaseFingerprint ($cases + $cases[0])) -eq $expected) { throw 'Extra duplicate row was not detected.' }
$checks += 'extra_row_detected'
$cases[1].Outcome = 'NotExecuted'
if ((Get-StabilityCaseFingerprint $cases) -eq $expected) { throw 'Unexpected skip was not detected.' }
$checks += 'changed_outcome_detected'
[pscustomobject]@{ Passed = $checks.Count; Failed = 0; Checks = $checks } | ConvertTo-Json
