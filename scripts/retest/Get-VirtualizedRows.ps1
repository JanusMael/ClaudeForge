#requires -Version 7
<#
    Get-VirtualizedRows.ps1 — enumerate a virtualized list by scrolling it.

    ⛔ WHY. Gap G8: a virtualized list exposes only its realized rows to UIA, so
       "not found" carries no information and absence cannot be asserted. Gap G9:
       the filter box, the usual workaround, ignores programmatic text. This walks
       the list with ScrollPattern instead, accumulating every row name that
       realizes at each position — which is the only route left that can actually
       ENUMERATE rather than merely confirm a guess.

    ⚠ It is a workaround, not a fix. See the proposal in
       docs/UIA-AUTOMATION-GAPS.md: ItemContainerPattern / VirtualizedItemPattern
       would make this unnecessary.

        pwsh -NoProfile -File scripts/retest/Get-VirtualizedRows.ps1 -Expect 'handoff,commit'
#>

[CmdletBinding()]
param(
    [string] $Expect,
    [int] $MaxScrolls = 60,
    [int] $SettleMilliseconds = 260
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UiaCommon.ps1"

[void] (Wait-ForgeSettled)

$TS   = [System.Windows.Automation.TreeScope]
$COND = [System.Windows.Automation.Condition]
$SP   = [System.Windows.Automation.ScrollPattern]

# Find every scrollable element, and drive the one with the most scrollable range.
$scrollables = [System.Collections.Generic.List[object]]::new()
foreach ($r in (Get-ForgeElements -Quiet)) {
    $p = $null
    if ($r.Element.TryGetCurrentPattern($SP::Pattern, [ref] $p)) {
        try {
            if ($p.Current.VerticallyScrollable) { $scrollables.Add([pscustomobject]@{ Row = $r; Pattern = $p }) }
        } catch { continue }
    }
}

Write-Host ('vertically scrollable elements: ' + $scrollables.Count)
if ($scrollables.Count -eq 0) { throw 'Nothing vertically scrollable found — is the list page open?' }

$target = $scrollables[0]
Write-Host ('driving: [{0}] {1} id={2}' -f $target.Row.Index, $target.Row.Type, $target.Row.AutomationId)

$names   = [System.Collections.Generic.HashSet[string]]::new()
$sources = @{}

function Add-VisibleRows {
    <#  Row identity is the "Open <name>" button each row carries. The row's SOURCE
        is the last Text node before the next row begins — capturing it is the point
        of E5, which asks whether each artifact is listed under the right source,
        not merely whether it is listed. #>
    $added = 0
    $rows = Get-ForgeElements -Quiet     # document order

    $pending = $null
    $lastText = $null
    foreach ($r in $rows) {
        if ($r.Type -eq 'Button' -and $r.Name -like 'Open *') {
            if ($pending -and $lastText) { $sources[$pending] = $lastText }
            $pending = $r.Name.Substring(5)
            $lastText = $null
            if ($names.Add($pending)) { $added++ }
            continue
        }
        if ($pending -and $r.Type -eq 'Text' -and -not [string]::IsNullOrWhiteSpace($r.Name)) {
            $lastText = $r.Name
        }
    }
    if ($pending -and $lastText -and -not $sources.ContainsKey($pending)) { $sources[$pending] = $lastText }

    return $added
}

# ⛔ STEP BY PERCENTAGE, NOT LargeIncrement. Measured: one LargeIncrement jumped
# from 0% to 84.2%, so rows between those positions never realized and the walk
# enumerated 34 of 111 while reporting a tidy "100%" finish. A scroll that reaches
# the end is not a scroll that saw everything.
$stepPercent = 100.0 / [Math]::Max(1, $MaxScrolls)

try { $target.Pattern.SetScrollPercent(-1, 0) } catch { }
Start-Sleep -Milliseconds $SettleMilliseconds
[void] (Add-VisibleRows)

for ($i = 1; $i -le $MaxScrolls; $i++) {
    $before = $names.Count
    $want = [Math]::Min(100.0, $i * $stepPercent)
    try { $target.Pattern.SetScrollPercent(-1, $want) } catch { break }
    Start-Sleep -Milliseconds $SettleMilliseconds
    [void] (Add-VisibleRows)

    $pct = -1
    try { $pct = [math]::Round($target.Pattern.Current.VerticalScrollPercent, 1) } catch { }
    if (($names.Count -ne $before) -or ($i % 10 -eq 0)) {
        Write-Host ('  step ' + $i + ': total=' + $names.Count + ' (+' + ($names.Count - $before) + ')  at ' + $pct + '%')
    }
    if ($want -ge 100) { break }
}

Write-Host ''
Write-Host ('======== ' + $names.Count + ' ROWS ENUMERATED ========')
foreach ($n in ($names | Sort-Object)) { Write-Host ('  ' + $n) }

if ($Expect) {
    Write-Host ''
    Write-Host '--- expected rows ---'
    $missing = 0
    foreach ($e in (Split-Needles -Needles $Expect)) {
        $hit = $names -contains $e
        if (-not $hit) { $missing++ }
        $src = if ($sources.ContainsKey($e)) { $sources[$e] } else { '(source not captured)' }
        $state = if ($hit) { 'FOUND' } else { 'MISSING' }
        Write-Host ('  {0,-28} {1,-8} source: {2}' -f $e, $state, $src)
    }
    Write-Host ''
    Write-Host ('missing: ' + $missing + ' of ' + (Split-Needles -Needles $Expect).Count)
}
