#requires -Version 7
<#
    Set-EditingScope.ps1 — switch the settings editor's scope (Local / Project / User).

    ⛔ DO THIS FIRST, IMMEDIATELY AFTER LAUNCH. Gap G4: the first popup expand in an
       app lifetime works; later ones return without error while
       ExpandCollapseState stays Collapsed and the item list never realises. Spending
       that one expand on something else means this script cannot run at all, and its
       failure looks like "the scope does not exist".

    Verifies the switch by re-reading the combo's value, so a silent no-op is reported
    rather than assumed.

        pwsh -NoProfile -File scripts/retest/Set-EditingScope.ps1 -Scope Project
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Local', 'Project', 'User')] [string] $Scope,
    [int] $SettleTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UiaCommon.ps1"

[void] (Wait-ForgeSettled -TimeoutSeconds $SettleTimeoutSeconds)

function Get-ScopeCombo {
    $rows = Get-ForgeElements -Quiet
    return ($rows | Where-Object { $_.Type -eq 'ComboBox' -and $_.Name -eq 'Editing scope' } | Select-Object -First 1)
}

$combo = Get-ScopeCombo
if (-not $combo) { throw 'Editing scope combo not found.' }

$before = Get-ForgeControlValue -Element $combo.Element
Write-Host ('Editing scope before: [' + $before + ']')
if ($before -eq $Scope) { Write-Host '  already there; nothing to do.'; return }

$ep = $null
if (-not $combo.Element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref] $ep)) {
    throw 'Scope combo exposes no ExpandCollapsePattern.'
}

$ep.Expand()
Start-Sleep -Milliseconds 700

if ($ep.Current.ExpandCollapseState -ne 'Expanded') {
    throw ('Expand() no-opped (state=' + $ep.Current.ExpandCollapseState + '). This is gap G4 — ' +
           'restart the app and run this script BEFORE any other expand. See docs/UIA-AUTOMATION-GAPS.md.')
}

$picked = $false
foreach ($item in $combo.Element.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)) {
    try {
        $t = $item.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        if ($t -ne 'ListItem' -or $item.Current.Name -ne $Scope) { continue }
        $sp = $null
        if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $sp)) {
            $sp.Select(); $picked = $true; break
        }
    } catch { continue }
}

if (-not $picked) {
    try { $ep.Collapse() } catch { }
    throw ('Scope "' + $Scope + '" was not offered. The dropdown realised no matching ListItem.')
}

Start-Sleep -Milliseconds 1500

# ⚠ Verify, never assume. A Select() that did nothing looks identical from here.
$after = Get-ForgeControlValue -Element (Get-ScopeCombo).Element
Write-Host ('Editing scope after : [' + $after + ']')
if ($after -ne $Scope) { throw ('Scope did not switch; wanted ' + $Scope + ', got ' + $after) }
Write-Host '  switched ✓'
