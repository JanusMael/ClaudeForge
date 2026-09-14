#!/usr/bin/env pwsh
# Audit-Accessibility.ps1 — walk a running app's UI Automation tree and report what a screen
# reader would actually hear.
#
# ══ WHY THIS EXISTS ══
#
# The headless test app is stripped of the App's resource dictionaries and cannot instantiate
# views, so no automated test in this repo can see a rendered control's accessible name. The
# AXAML guards check that `AutomationProperties.Name` is PRESENT in markup; they cannot check
# what the platform ends up exposing, which is a different question with its own traps —
# a Name on a TextBlock is ignored in favour of its Text, a composite control's name has to sit
# on the element that takes focus, and a binding that fails renders as the view-model's type name.
#
# This drives the real, published app through the same API a screen reader uses.
#
# ⚠ WINDOWS ONLY. UI Automation is a Windows API; this is a substitute for a screen-reader pass
# on the one platform where one can be automated, not a cross-platform guarantee.
#
# USAGE
#   pwsh -NoProfile -File scripts/Audit-Accessibility.ps1 -ProcessName ClaudeForge
#   pwsh -NoProfile -File scripts/Audit-Accessibility.ps1 -ProcessName OpenCodeForge -MaxDepth 40
#
# ⓘ Open the windows you want covered BEFORE running: every top-level window of the process is
# audited, so press F12 and Shift+F12 first to include the diagnostics windows.

[CmdletBinding()]
param(
    [string] $ProcessName = 'ClaudeForge',
    [int] $MaxDepth = 30,
    [int] $MaxElements = 6000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# Control types a user can operate. A missing name on one of these is a real defect; a missing
# name on a decorative Text or Group is usually not.
$InteractiveTypes = @(
    'Button', 'CheckBox', 'ComboBox', 'Edit', 'Hyperlink', 'ListItem', 'MenuItem',
    'RadioButton', 'Slider', 'SplitButton', 'Spinner', 'Tab', 'TabItem', 'TreeItem'
)

<#
.SYNOPSIS
    True when a name is really the leak of an internal type rather than a label.
#>
function Test-LooksLikeTypeName {
    param([string] $Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return $false
    }

    # ⛔ A VERSION IS NOT A TYPE NAME, and the dotted-segments rule below cannot tell them apart.
    # Caught on this script's first real run: it reported the status bar's "v2026.3.914.1513" as a
    # leaked type, because four dot-separated alphanumeric segments is exactly what it matches.
    # Excluded first, so the rule that follows stays broad.
    if ($Name -match '^[vV]?\d+(\.\d+)+$') {
        return $false
    }

    # The measured failure mode in this repo: a row announcing its view-model's type, e.g.
    # "Bennewitz.Ninja.ClaudeForge.ViewModels.FooViewModel". Also catches bare "FooViewModel".
    return ($Name -match 'ViewModel$') -or
           ($Name -match '^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+){2,}$') -or
           ($Name -match '^Bennewitz\.')
}

function Get-TopLevelWindows {
    param([string] $Name)

    $procs = @(Get-Process -Name $Name -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) {
        throw ("No process named '" + $Name + "' is running. Start the app first.")
    }

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $windows = @()

    foreach ($proc in $procs) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)

        $found = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
        foreach ($w in $found) {
            $windows += $w
        }
    }

    return $windows
}

function Invoke-Walk {
    param(
        [System.Windows.Automation.AutomationElement] $Element,
        [string] $WindowName,
        [int] $Depth,
        [System.Collections.ArrayList] $Findings,
        [ref] $Counter
    )

    if ($Depth -gt $MaxDepth -or $Counter.Value -ge $MaxElements) {
        return
    }

    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $child = $walker.GetFirstChild($Element)

    while ($null -ne $child -and $Counter.Value -lt $MaxElements) {
        $Counter.Value++

        try {
            $current = $child.Current
            $type = $current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
            $name = $current.Name
            $focusable = $current.IsKeyboardFocusable

            $problem = $null
            if (Test-LooksLikeTypeName -Name $name) {
                $problem = 'announces a type name'
            }
            elseif ([string]::IsNullOrWhiteSpace($name)) {
                if ($InteractiveTypes -contains $type) {
                    $problem = 'interactive, no accessible name'
                }
                elseif ($focusable) {
                    $problem = 'focusable, no accessible name'
                }
            }

            if ($problem) {
                $null = $Findings.Add([pscustomobject] @{
                        Window = $WindowName
                        Type = $type
                        AutomationId = $current.AutomationId
                        Name = $name
                        Focusable = $focusable
                        Problem = $problem
                    })
            }
        }
        catch {
            # An element can vanish mid-walk (virtualised lists rebuild constantly). Skipping one
            # is correct; aborting the audit over it is not.
        }

        Invoke-Walk -Element $child -WindowName $WindowName -Depth ($Depth + 1) `
            -Findings $Findings -Counter $Counter

        $child = $walker.GetNextSibling($child)
    }
}

function Main {
    $windows = @(Get-TopLevelWindows -Name $ProcessName)
    Write-Host ('Top-level windows: ' + $windows.Count) -ForegroundColor Cyan

    $findings = [System.Collections.ArrayList]::new()
    $counter = 0

    foreach ($window in $windows) {
        $title = $window.Current.Name
        if ([string]::IsNullOrWhiteSpace($title)) {
            $title = '(untitled window)'
        }

        Write-Host ('  walking: ' + $title)
        Invoke-Walk -Element $window -WindowName $title -Depth 0 `
            -Findings $findings -Counter ([ref] $counter)
    }

    Write-Host ''
    Write-Host ('Elements visited: ' + $counter)
    Write-Host ('Findings: ' + $findings.Count) -ForegroundColor (
        $(if ($findings.Count -eq 0) { 'Green' } else { 'Yellow' }))

    if ($findings.Count -gt 0) {
        Write-Host ''
        $findings |
            Group-Object Window, Problem, Type |
            Sort-Object Count -Descending |
            ForEach-Object {
                $first = $_.Group[0]
                $line = '  [' + $_.Count + '] ' + $first.Problem + '  type=' + $first.Type +
                    '  window=' + $first.Window
                if ($first.Name) { $line += '  name="' + $first.Name + '"' }
                if ($first.AutomationId) { $line += '  id=' + $first.AutomationId }
                Write-Host $line
            }
    }
}

# Guarded on an env var rather than $MyInvocation.InvocationName: the VS Code extension
# dot-sources on F5, where InvocationName IS '.', so the conventional guard never runs Main.
if (-not $env:AUDIT_ACCESSIBILITY_NOEXEC) {
    Main
}
