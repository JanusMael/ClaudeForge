#requires -Version 7
<#
    Invoke-UiElement.ps1 — act on one ClaudeForge control, by UIA pattern.

    ⛔ PATTERNS, NEVER COORDINATE CLICKS. The app is not the foreground window
       while an agent drives it, so a synthesised click lands nowhere and the
       script "succeeds" having done nothing. Every action here goes through
       Invoke / SelectionItem / Value, which are focus-independent.

    Examples:
        # navigate
        pwsh -NoProfile -File scripts/retest/Invoke-UiElement.ps1 `
            -Name 'Backup / Restore' -Type TreeItem -Action Select

        # press a button
        pwsh -NoProfile -File scripts/retest/Invoke-UiElement.ps1 `
            -Name 'Save all settings' -Type Button -Action Invoke

        # type into a field
        pwsh -NoProfile -File scripts/retest/Invoke-UiElement.ps1 `
            -Name 'model' -AutomationId PART_TextBox -Action SetValue -Value 'sonnet'

    ⚠ -Name is matched EXACTLY by default because Names here are localized and a
      substring match across a localized tree is how a harness ends up driving the
      wrong control. Use -Contains when you genuinely want substring matching.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Name,
    [string] $Type,
    [string] $AutomationId,
    [ValidateSet('Select', 'Invoke', 'SetValue', 'Expand', 'Collapse', 'Read')]
    [string] $Action = 'Read',
    [string] $Value,
    [switch] $Contains,
    [int] $SettleTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/UiaCommon.ps1"

[void] (Wait-ForgeSettled -TimeoutSeconds $SettleTimeoutSeconds)

$rows = Get-ForgeElements
$candidates = @(
    $rows | Where-Object {
        $nameOk = if ($Contains) { $_.Name -like ('*' + $Name + '*') } else { $_.Name -eq $Name }
        $typeOk = (-not $Type) -or ($_.Type -eq $Type)
        $idOk   = (-not $AutomationId) -or ($_.AutomationId -eq $AutomationId)
        $nameOk -and $typeOk -and $idOk
    }
)

if ($candidates.Count -eq 0) {
    throw ('No element matched Name=' + $Name + ' Type=' + $Type + ' AutomationId=' + $AutomationId)
}
if ($candidates.Count -gt 1) {
    Write-Host ('⚠ ' + $candidates.Count + ' elements matched; acting on the FIRST. Narrow with -Type/-AutomationId:') -ForegroundColor Yellow
    foreach ($c in $candidates) {
        Write-Host ('    [{0,3}] {1,-11} id={2,-22} {3}' -f $c.Index, $c.Type, $c.AutomationId, $c.Name)
    }
}

$target = $candidates[0]
$el = $target.Element
Write-Host ('Target: [{0}] {1} id={2} name={3}' -f $target.Index, $target.Type, $target.AutomationId, $target.Name)

switch ($Action) {
    'Read' {
        $v = Get-ForgeControlValue -Element $el
        Write-Host ('  VALUE=[' + $v + ']')
        Write-Host ('  IsEnabled=' + $el.Current.IsEnabled + '  IsOffscreen=' + $el.Current.IsOffscreen)
    }
    'Invoke' {
        $p = $null
        if (-not $el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $p)) {
            throw 'Element has no InvokePattern.'
        }
        $p.Invoke()
        Write-Host '  invoked'
    }
    'Select' {
        $p = $null
        if (-not $el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $p)) {
            throw 'Element has no SelectionItemPattern.'
        }
        $p.Select()
        Write-Host '  selected'
    }
    'SetValue' {
        if ($null -eq $Value) { throw '-Value is required for SetValue.' }
        $p = $null
        if (-not $el.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $p)) {
            throw 'Element has no ValuePattern.'
        }
        Write-Host ('  before=[' + $p.Current.Value + ']')
        $p.SetValue($Value)
        Start-Sleep -Milliseconds 1200
        Write-Host ('  after =[' + $p.Current.Value + ']')

        # ⚠ Confirm the BINDING saw it, not just the control. Without this a
        # later save is vacuous and reports success having written nothing.
        $d = Test-ForgeDirty
        Write-Host ('  title=' + $d.Title)
        Write-Host ('  dirty=' + $d.Dirty)
    }
    'Expand' {
        $p = $null
        if (-not $el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref] $p)) {
            throw 'Element has no ExpandCollapsePattern.'
        }
        $p.Expand(); Start-Sleep -Milliseconds 600
        Write-Host ('  state=' + $p.Current.ExpandCollapseState)
        if ($p.Current.ExpandCollapseState -ne 'Expanded') {
            Write-Host '  ⛔ Expand() returned but the state did not change — see docs/UIA-AUTOMATION-GAPS.md G4. Restart the app.' -ForegroundColor Red
        }
    }
    'Collapse' {
        $p = $null
        if (-not $el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref] $p)) {
            throw 'Element has no ExpandCollapsePattern.'
        }
        $p.Collapse()
        Write-Host ('  state=' + $p.Current.ExpandCollapseState)
    }
}
