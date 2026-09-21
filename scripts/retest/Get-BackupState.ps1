#requires -Version 7
<#
    Get-BackupState.ps1 — report the Backup page's real state: which mode radio is
    selected, which targets are checked, and any progress/status text.

    ⚠ Exists because the radio LABELS are visible in a tree dump while the
    SELECTION is not: reading names alone tells you three modes exist, never which
    one is armed. A retest that assumes "Settings only (fast, default)" is selected
    because the label says "default" can silently archive ~/.claude/projects.
#>

[CmdletBinding()]
param([int] $SettleTimeoutSeconds = 60)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UiaCommon.ps1"

[void] (Wait-ForgeSettled -TimeoutSeconds $SettleTimeoutSeconds)
$rows = Get-ForgeElements

Write-Host ''
Write-Host '--- mode radios ---'
foreach ($r in ($rows | Where-Object { $_.Type -eq 'RadioButton' })) {
    $sel = '?'
    $p = $null
    if ($r.Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $p)) {
        $sel = $p.Current.IsSelected
    }
    $short = if ($r.Name.Length -gt 60) { $r.Name.Substring(0, 60) + '…' } else { $r.Name }
    Write-Host ('  selected={0,-6} {1}' -f $sel, $short)
}

Write-Host ''
Write-Host '--- target checkboxes ---'
foreach ($r in ($rows | Where-Object { $_.Type -eq 'CheckBox' })) {
    $state = '?'
    $p = $null
    if ($r.Element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref] $p)) {
        $state = $p.Current.ToggleState
    }
    Write-Host ('  {0,-10} {1}' -f $state, $r.Name)
}

Write-Host ''
Write-Host '--- buttons that indicate work in flight ---'
foreach ($r in ($rows | Where-Object { $_.Type -eq 'Button' -and $_.Name -in @('Cancel', 'Create Backup', 'Restore') })) {
    Write-Host ('  {0,-16} enabled={1}' -f $r.Name, $r.Element.Current.IsEnabled)
}

Write-Host ''
Write-Host '--- status / progress text ---'
$keywords = @('Backing', 'Backup', 'Restor', 'progress', '%', 'Creating', 'Wrote', 'Saved', 'complete', 'Cancel', 'failed', 'error')
foreach ($r in ($rows | Where-Object { $_.Type -in @('Text', 'ProgressBar') })) {
    if ([string]::IsNullOrWhiteSpace($r.Name)) { continue }
    foreach ($k in $keywords) {
        if ($r.Name -like ('*' + $k + '*')) {
            $short = if ($r.Name.Length -gt 110) { $r.Name.Substring(0, 110) + '…' } else { $r.Name }
            Write-Host ('  [{0,3}] {1,-12} {2}' -f $r.Index, $r.Type, $short)
            break
        }
    }
}
