#requires -Version 7
<#
    E2 driver — set `model` at one scope and save, entirely through UIA patterns.

    ⚠ Clicks are avoided deliberately: the app is not foreground while an agent
    drives it, so coordinate clicks land nowhere. Every interaction here is a
    UIA pattern (Selection / Value / Invoke), which works regardless of focus.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('Local', 'Project', 'User')] [string] $Scope,
    [Parameter(Mandatory)] [string] $ModelValue
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$AE   = [System.Windows.Automation.AutomationElement]
$TS   = [System.Windows.Automation.TreeScope]
$COND = [System.Windows.Automation.Condition]
$VP   = [System.Windows.Automation.ValuePattern]
$EXP  = [System.Windows.Automation.ExpandCollapsePattern]
$SI   = [System.Windows.Automation.SelectionItemPattern]
$INV  = [System.Windows.Automation.InvokePattern]

function Get-Win {
    $proc = @(Get-Process ClaudeForge -ErrorAction SilentlyContinue)[0]
    if (-not $proc) { throw 'ClaudeForge is not running.' }
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    return @($AE::RootElement.FindAll($TS::Children, $c))
}

function Find-One {
    param([object[]] $Windows, [string] $Type, [string] $Name, [string] $AutoId)
    foreach ($w in $Windows) {
        foreach ($e in $w.FindAll($TS::Descendants, $COND::TrueCondition)) {
            try {
                $t = $e.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
                if ($Type -and $t -ne $Type) { continue }
                if ($Name -and $e.Current.Name -ne $Name) { continue }
                if ($AutoId -and $e.Current.AutomationId -ne $AutoId) { continue }
                return $e
            } catch { continue }
        }
    }
    return $null
}

$wins = Get-Win
Write-Host ('Window: ' + $wins[0].Current.Name)

# ── 1 · Select the editing scope ─────────────────────────────────────────────
$combo = Find-One -Windows $wins -Type 'ComboBox' -Name 'Editing scope'
if (-not $combo) { throw 'Editing scope combo not found.' }

$p = $null
if (-not $combo.TryGetCurrentPattern($EXP::Pattern, [ref] $p)) { throw 'No ExpandCollapse on scope combo.' }
$p.Expand(); Start-Sleep -Milliseconds 500

$picked = $false
foreach ($item in $combo.FindAll($TS::Descendants, $COND::TrueCondition)) {
    try {
        $t = $item.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        if ($t -ne 'ListItem' -or $item.Current.Name -ne $Scope) { continue }
        $sp = $null
        if ($item.TryGetCurrentPattern($SI::Pattern, [ref] $sp)) { $sp.Select(); $picked = $true; break }
    } catch { continue }
}
if (-not $picked) { $p.Collapse(); throw ('Could not select scope: ' + $Scope) }
Start-Sleep -Milliseconds 1200

$now = (Find-One -Windows (Get-Win) -Type 'ComboBox' -Name 'Editing scope')
$nowVal = $null
$vp2 = $null
if ($now.TryGetCurrentPattern($VP::Pattern, [ref] $vp2)) { $nowVal = $vp2.Current.Value }
Write-Host ('Editing scope is now: [' + $nowVal + ']')
if ($nowVal -ne $Scope) { throw ('Scope did not switch; wanted ' + $Scope + ' got ' + $nowVal) }

# ── 2 · Set the model value ──────────────────────────────────────────────────
$wins = Get-Win
$box = $null
foreach ($w in $wins) {
    foreach ($e in $w.FindAll($TS::Descendants, $COND::TrueCondition)) {
        try {
            if ($e.Current.AutomationId -ne 'PART_TextBox') { continue }
            if ($e.Current.Name -ne 'model') { continue }
            $box = $e; break
        } catch { continue }
    }
    if ($box) { break }
}
if (-not $box) { throw 'model textbox not found (is the Model & Effort page open?).' }

$bvp = $null
if (-not $box.TryGetCurrentPattern($VP::Pattern, [ref] $bvp)) { throw 'model box has no ValuePattern.' }
Write-Host ('model box before: [' + $bvp.Current.Value + ']')
$bvp.SetValue($ModelValue)
Start-Sleep -Milliseconds 1500
Write-Host ('model box after : [' + $bvp.Current.Value + ']')

# ── 3 · Confirm the app registered a pending change ──────────────────────────
# The window title gains " *" when HasUnsavedChanges is true. If it does not,
# SetValue moved the text without the binding seeing it, and a "save" below
# would prove nothing.
$title = (Get-Win)[0].Current.Name
Write-Host ('Title after edit: ' + $title)
$dirty = $title.TrimEnd().EndsWith('*')
Write-Host ('Dirty marker present: ' + $dirty)
if (-not $dirty) {
    Write-Host 'ABORT: no pending change registered — not saving, because the save would be vacuous.' -ForegroundColor Red
    exit 2
}

# ── 4 · Save ─────────────────────────────────────────────────────────────────
$save = Find-One -Windows (Get-Win) -Type 'Button' -Name 'Save all settings'
if (-not $save) { throw 'Save button not found.' }
$ip = $null
if (-not $save.TryGetCurrentPattern($INV::Pattern, [ref] $ip)) { throw 'Save button has no InvokePattern.' }
$ip.Invoke()
Start-Sleep -Milliseconds 1800

# ── 5 · The save-confirmation dialog ─────────────────────────────────────────
Write-Host ''
Write-Host 'Looking for a save dialog…'
$dialogButtons = [System.Collections.Generic.List[string]]::new()
foreach ($w in Get-Win) {
    foreach ($e in $w.FindAll($TS::Descendants, $COND::TrueCondition)) {
        try {
            $t = $e.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
            if ($t -ne 'Button') { continue }
            $n = $e.Current.Name
            if ($n -match '^(Save|Cancel|Don.t save|OK|Yes|No)$') { $dialogButtons.Add($n) }
        } catch { continue }
    }
}
Write-Host ('Dialog-ish buttons visible: ' + ($dialogButtons -join ', '))

foreach ($w in Get-Win) {
    foreach ($e in $w.FindAll($TS::Descendants, $COND::TrueCondition)) {
        try {
            $t = $e.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
            if ($t -ne 'Button' -or $e.Current.Name -ne 'Save') { continue }
            $dp = $null
            if ($e.TryGetCurrentPattern($INV::Pattern, [ref] $dp)) {
                Write-Host 'Invoking dialog Save…'
                $dp.Invoke()
                Start-Sleep -Milliseconds 1500
            }
        } catch { continue }
    }
}

Start-Sleep -Milliseconds 1000
Write-Host ('Final title: ' + (Get-Win)[0].Current.Name)
