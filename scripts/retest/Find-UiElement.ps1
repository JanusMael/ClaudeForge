#requires -Version 7
<#
    Find-UiElement.ps1 — list ClaudeForge UI elements whose Name matches a needle.

    The workhorse for "is this control present, and what is it called?" during a
    retest. Settles the tree first, and always reports the seen/named/threw
    counters so a zero-match result can be told apart from a degraded tree.

    Examples:
        pwsh -NoProfile -File scripts/retest/Find-UiElement.ps1 -Needles 'Backup,Restore'
        pwsh -NoProfile -File scripts/retest/Find-UiElement.ps1 -Needles '*' -InteractiveOnly
        pwsh -NoProfile -File scripts/retest/Find-UiElement.ps1 -Needles 'model' -ShowValues

    ⛔ Pass several needles COMMA-JOINED IN ONE ARGUMENT ('a,b,c'). `pwsh -File`
       does not parse PowerShell syntax, so -Needles 'a','b','c' arrives as the
       single string "a,b,c" anyway; this script splits on commas so both spellings
       behave identically. Getting this wrong produced 0 matches against a healthy
       tree and looked exactly like a missing control.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $Needles,
    [switch] $InteractiveOnly,
    [switch] $ShowValues,
    [int] $SettleTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UiaCommon.ps1"

$interactive = @(
    'Button', 'ComboBox', 'Edit', 'ListItem', 'TreeItem', 'CheckBox',
    'RadioButton', 'Tab', 'TabItem', 'MenuItem', 'Hyperlink', 'Slider'
)

$needleList = Split-Needles -Needles $Needles
Write-Host ('Needles: ' + ($needleList -join ' | '))

[void] (Wait-ForgeSettled -TimeoutSeconds $SettleTimeoutSeconds)

Write-Host ('WINDOW: ' + (Get-ForgeWindow).Current.Name)
$rows = Get-ForgeElements

$matched = 0
foreach ($r in $rows) {
    if ([string]::IsNullOrWhiteSpace($r.Name)) { continue }
    if ($InteractiveOnly -and ($interactive -notcontains $r.Type)) { continue }

    $hit = $false
    foreach ($n in $needleList) {
        if ($r.Name -like ('*' + $n + '*')) { $hit = $true; break }
    }
    if (-not $hit) { continue }

    $matched++
    $short = if ($r.Name.Length -gt 90) { $r.Name.Substring(0, 90) + '…' } else { $r.Name }
    $line = '  [{0,3}] {1,-11} id={2,-24} {3}' -f $r.Index, $r.Type, $r.AutomationId, $short

    if ($ShowValues) {
        $v = Get-ForgeControlValue -Element $r.Element
        if ($null -ne $v) { $line += ('   VALUE=[' + $v + ']') }
    }
    Write-Host $line
}

Write-Host ''
Write-Host ('matched: ' + $matched)
