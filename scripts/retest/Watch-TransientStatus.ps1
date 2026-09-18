#requires -Version 7
<#
    Watch-TransientStatus.ps1 — capture short-lived UI (the centre status pill) that a
    settle-based probe cannot see.

    ⛔ WHY THIS EXISTS. `Wait-ForgeSettled` waits for the descendant count to hold steady
       across samples — which is correct for reading a page, and exactly wrong for a status
       pill that appears and clears in ~6s. Measured: the tree went 274 → 276 → 274 across
       one settle, so the pill existed and was gone before enumeration began. The probe
       reported "no pill" for a pill that had worked.

    Invokes a control, then polls WITHOUT settling, recording every text node that appears
    and when it disappears — so the report is "what showed, and for how long".

        pwsh -NoProfile -File scripts/retest/Watch-TransientStatus.ps1 `
            -InvokeName 'Share Config' -InvokeType Button
#>

[CmdletBinding()]
param(
    [string] $InvokeName,
    [string] $InvokeType = 'Button',
    [int] $WatchSeconds = 12,
    [int] $PollMilliseconds = 250
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UiaCommon.ps1"

function Get-TextSnapshot {
    $set = [System.Collections.Generic.HashSet[string]]::new()
    try {
        foreach ($w in Get-ForgeWindows) {
            foreach ($e in $w.FindAll(
                        [System.Windows.Automation.TreeScope]::Descendants,
                        [System.Windows.Automation.Condition]::TrueCondition)) {
                try {
                    $t = $e.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
                    if ($t -ne 'Text') { continue }
                    $n = $e.Current.Name
                    if (-not [string]::IsNullOrWhiteSpace($n)) { [void] $set.Add($n) }
                } catch { continue }
            }
        }
    } catch { }
    return $set
}

Write-Host 'Taking baseline snapshot (no settle — speed matters here)…'
$baseline = Get-TextSnapshot
Write-Host ('  baseline text nodes: ' + $baseline.Count)

if ($InvokeName) {
    $rows = Get-ForgeElements -Quiet
    $target = $rows | Where-Object { $_.Name -eq $InvokeName -and $_.Type -eq $InvokeType } | Select-Object -First 1
    if (-not $target) { throw ('Control not found: ' + $InvokeName) }

    $p = $null
    if (-not $target.Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $p)) {
        throw 'Control has no InvokePattern.'
    }
    Write-Host ('Invoking: ' + $InvokeName)
    $p.Invoke()
}

$seenAt   = @{}
$goneAt   = @{}
$sw = [System.Diagnostics.Stopwatch]::StartNew()

while ($sw.Elapsed.TotalSeconds -lt $WatchSeconds) {
    $now = Get-TextSnapshot
    foreach ($n in $now) {
        if (-not $baseline.Contains($n) -and -not $seenAt.ContainsKey($n)) {
            $seenAt[$n] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
            Write-Host ('  +' + $seenAt[$n] + 's  APPEARED: ' + $n) -ForegroundColor Green
        }
    }
    foreach ($n in @($seenAt.Keys)) {
        if (-not $now.Contains($n) -and -not $goneAt.ContainsKey($n)) {
            $goneAt[$n] = [math]::Round($sw.Elapsed.TotalSeconds, 2)
            Write-Host ('  +' + $goneAt[$n] + 's  CLEARED : ' + $n) -ForegroundColor DarkGray
        }
    }
    Start-Sleep -Milliseconds $PollMilliseconds
}

Write-Host ''
Write-Host '======== TRANSIENT ELEMENTS ========'
if ($seenAt.Count -eq 0) { Write-Host '  (nothing appeared)' }
foreach ($n in $seenAt.Keys) {
    $gone = if ($goneAt.ContainsKey($n)) { $goneAt[$n].ToString() + 's' } else { 'still present at end of watch' }
    Write-Host ('  appeared +' + $seenAt[$n] + 's, cleared ' + $gone)
    Write-Host ('    "' + $n + '"')
}
