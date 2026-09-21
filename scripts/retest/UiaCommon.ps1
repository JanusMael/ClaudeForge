#requires -Version 7
<#
    UiaCommon.ps1 — shared UI Automation helpers for driving ClaudeForge during a
    manual-retest pass (plans/00003 Phase B and successors).

    ⛔ THIS FILE IS DOT-SOURCED. It therefore declares NO param() block and calls
       NO bare exit: a param() default would overwrite the caller's variables of
       the same name, and an exit inside an editor host kills the host rather
       than the script. Callers own both.

    Usage:
        . "$PSScriptRoot/UiaCommon.ps1"
        $win = Get-ForgeWindow
        Wait-ForgeSettled

    ── THREE THINGS THAT COST TIME BEFORE THEY WERE WRITTEN DOWN ───────────────

    1. ⛔ A FIXED SLEEP IS NOT A MEASUREMENT. Measured on one cold single-file
       launch: 72 descendants at 5.3s, a COMException at 7.1s, 168 at 8.0s.
       Always Wait-ForgeSettled before reading anything, or the tree reports
       "element missing" when it means "not built yet".

    2. ⛔ `pwsh -File script.ps1 -Needles 'a','b'` passes ONE literal string
       "a,b" — -File does not parse PowerShell syntax. Array parameters must be
       split inside the script (see Split-Needles). This silently produced
       "0 matches" against a healthy tree.

    3. ⛔ NEVER `catch { continue }` around $e.Current.* without counting. If the
       COM state degrades, EVERY element throws and the loop reports "no
       matches" — indistinguishable from a healthy tree with nothing to match.
       Report seen/named/matched/threw, always.
#>

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# Literal type references, never a variable holding the type. `$VP::Pattern`
# resolved to $null in one session while the identical expression worked in
# another script; the literal form has not failed.
$script:AE   = [System.Windows.Automation.AutomationElement]
$script:TS   = [System.Windows.Automation.TreeScope]
$script:COND = [System.Windows.Automation.Condition]

function Split-Needles {
    <#  Works around gotcha 2: accepts either a real array or the single
        comma-joined string that `pwsh -File` produces. #>
    param([string[]] $Needles)
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($n in $Needles) {
        foreach ($part in ($n -split ',')) {
            $t = $part.Trim()
            if ($t) { $out.Add($t) }
        }
    }
    return $out
}

function Get-ForgeWindows {
    $procs = @(Get-Process ClaudeForge -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { throw 'ClaudeForge is not running.' }
    $wins = [System.Collections.Generic.List[object]]::new()
    foreach ($p in $procs) {
        $c = New-Object System.Windows.Automation.PropertyCondition(
            $script:AE::ProcessIdProperty, $p.Id)
        foreach ($w in $script:AE::RootElement.FindAll($script:TS::Children, $c)) { $wins.Add($w) }
    }
    return $wins
}

function Get-ForgeWindow {
    return (Get-ForgeWindows)[0]
}

function Wait-ForgeSettled {
    <#  Blocks until the descendant count is stable across N samples. Returns the
        settled count so a caller can assert on it rather than assume. #>
    param([int] $TimeoutSeconds = 60, [int] $RequiredStable = 3)

    $stable = 0; $last = -1; $count = 0
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $count = 0
            foreach ($w in Get-ForgeWindows) {
                $count += $w.FindAll($script:TS::Descendants, $script:COND::TrueCondition).Count
            }
        } catch { Start-Sleep -Milliseconds 700; continue }

        if ($count -eq $last -and $count -gt 0) { $stable++ } else { $stable = 0 }
        $last = $count
        if ($stable -ge $RequiredStable) { break }
        Start-Sleep -Milliseconds 700
    }
    Write-Host ('(settled at ' + $count + ' descendants)')
    return $count
}

function Get-ForgeElements {
    <#  Every descendant of every window, as {Index,Type,AutomationId,Name,Element},
        plus the diagnostic counters gotcha 3 exists for. #>
    param([switch] $Quiet)

    $rows  = [System.Collections.Generic.List[object]]::new()
    $seen = 0; $named = 0; $threw = 0

    # ⚠ FindAll intermittently throws "Unexpected HRESULT ... COM component" on
    # this app's tree (gap G4). It is transient, so it is retried — but the
    # COLLECTION must happen INSIDE the retry. An earlier version probed with one
    # traversal inside the try and then re-enumerated outside it, which left the
    # real work unprotected and threw anyway. Collect once, reuse the rows.
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        $rows.Clear(); $seen = 0; $named = 0; $threw = 0
        try {
            foreach ($w in Get-ForgeWindows) {
                foreach ($e in $w.FindAll($script:TS::Descendants, $script:COND::TrueCondition)) {
                    $seen++
                    try {
                        $name = $e.Current.Name
                        $type = $e.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
                        $id   = $e.Current.AutomationId
                    } catch { $threw++; continue }

                    if (-not [string]::IsNullOrWhiteSpace($name)) { $named++ }
                    $rows.Add([pscustomobject]@{
                        Index = $seen; Type = $type; AutomationId = $id; Name = $name; Element = $e
                    })
                }
            }
            break
        } catch {
            if ($attempt -eq 4) { throw }
            Write-Host ('  (COM hiccup on FindAll; retry ' + $attempt + '/3)') -ForegroundColor DarkYellow
            Start-Sleep -Milliseconds (600 * $attempt)
        }
    }

    if (-not $Quiet) {
        Write-Host ('  -- seen=' + $seen + ' named=' + $named + ' threw=' + $threw)
        if ($threw -gt 0 -and $threw -ge ($seen / 2)) {
            Write-Host '  ⛔ More than half the elements threw. The tree is degraded, NOT empty — restart the app.' -ForegroundColor Red
        }
    }
    return $rows
}

function Get-ForgeControlValue {
    param([Parameter(Mandatory)] $Element)
    $p = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $p)) {
        return $p.Current.Value
    }
    return $null
}

function Test-ForgeDirty {
    <#  The window title gains " *" while there are unsaved changes. This is the
        only cheap way to confirm an automated edit actually reached the binding
        — without it, a subsequent "save" proves nothing. #>
    $title = (Get-ForgeWindow).Current.Name
    return @{ Title = $title; Dirty = $title.TrimEnd().EndsWith('*') }
}
