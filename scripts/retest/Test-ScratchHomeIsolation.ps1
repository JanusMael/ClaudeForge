#Requires -Version 7.0
<#
.SYNOPSIS
    Prove that a ClaudeForge run honours CLAUDE_CONFIG_DIR and writes nothing to the real home.

.DESCRIPTION
    plans/00002 step 6's verification, and the dividend the plan predicted: with this green, the
    write-path retest items (E1-E3) can be driven against a scratch home instead of the tester's
    own ~/.claude.

    Both halves are asserted, because either alone is worthless:

      1. The scratch home is actually USED -- ClaudeForge's artifacts appear there. Without this,
         "the real home was untouched" is equally satisfied by an app that never started.
      2. ClaudeForge wrote NOTHING to the real home -- its artifacts there are unchanged in size
         and last-write time across the run.

    The real home is only ever READ. Nothing here writes to it, and a difference is reported
    rather than corrected.

    A whole-home before/after diff CANNOT be used, and that is measured rather than assumed. A
    control window with no app running at all showed 8 changes in 25 seconds: session transcripts
    under projects/, a ~/.claude.json backup rotation, and a file-history entry. The real home is
    NOT quiescent while Claude Code is running, so raw churn is unattributable -- the first draft
    of this check reported a false FAIL on exactly that. The comparison is therefore scoped to the
    artifacts ClaudeForge itself writes, by name.

.PARAMETER Exe
    The ClaudeForge executable to launch. A published build, not a Debug one, if the point is to
    measure what ships.

.PARAMETER ScratchHome
    Directory to point CLAUDE_CONFIG_DIR at. Created if absent. Use a fresh one per run: a
    populated scratch home cannot show that THIS run wrote anything.

.PARAMETER Seconds
    How long to let the app run before closing it. The default is generous because the UIA tree
    is unstable during startup and the schema chain can spend its fetch timeout.

.EXAMPLE
    pwsh -NoProfile -File scripts/retest/Test-ScratchHomeIsolation.ps1 `
        -Exe src/ClaudeForge/bin/Release/net10.0/win-x64/publish/ClaudeForge.exe `
        -ScratchHome $env:TEMP/cf-scratch-1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $ScratchHome,
    [int] $Seconds = 25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The files ClaudeForge writes under a Claude home, relative to the home root.
#
# cache/model-catalog/tok-*-ccd.json is deliberately ABSENT: those are written by Claude Code
# itself, on its own schedule. Including them would attribute Claude Code's cache to ClaudeForge
# -- verified by timestamp, one landed 75 seconds before the app under test was launched.
$script:Artifacts = @(
    'cache/ClaudeForge-gui-state.json'
    'cache/schema-snapshot-claude-code-settings.json'
    'cache/schema-snapshot-claude-desktop-config.json'
    'cache/schemas/claude-code-settings.json'
    'cache/schemas/claude-code-settings.json.meta.json'
    'cache/schemas/claude-desktop-config.json'
    'cache/schemas/claude-desktop-config.json.meta.json'
)

function Get-ArtifactSnapshot {
    param([string] $Root)

    $snap = @{}
    foreach ($rel in $script:Artifacts) {
        $full = Join-Path $Root $rel
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            $item = Get-Item -LiteralPath $full
            # Ticks, not a formatted string: a general-format datetime carries no sub-second
            # component, so two writes inside one second would compare equal.
            $snap[$rel] = ('{0}:{1}' -f $item.Length, $item.LastWriteTimeUtc.Ticks)
        }
        else {
            $snap[$rel] = $null
        }
    }

    $snap
}

function Invoke-ScratchHomeCheck {
    $realHome = Join-Path $HOME '.claude'
    $exePath = (Resolve-Path -LiteralPath $Exe).Path

    Write-Host ('real home : ' + $realHome)
    Write-Host ('scratch   : ' + $ScratchHome)
    Write-Host ''

    New-Item -ItemType Directory -Force -Path $ScratchHome | Out-Null

    $realBefore = Get-ArtifactSnapshot -Root $realHome
    $scratchBefore = Get-ArtifactSnapshot -Root $ScratchHome

    $presentReal = @($realBefore.Values | Where-Object { $_ }).Count
    $presentScratch = @($scratchBefore.Values | Where-Object { $_ }).Count

    # A real home holding NONE of these would make "0 touched" true for the wrong reason.
    Write-Host ('ClaudeForge artifacts already in the real home: ' +
        $presentReal + '/' + $script:Artifacts.Count)
    Write-Host ('already in scratch: ' + $presentScratch + '/' + $script:Artifacts.Count)
    if ($presentScratch -gt 0) {
        Write-Warning ('The scratch home is not empty, so this run cannot prove it wrote ' +
            'anything. Use a fresh directory.')
    }
    Write-Host ''

    $previous = $env:CLAUDE_CONFIG_DIR
    $peak = 0
    try {
        $env:CLAUDE_CONFIG_DIR = $ScratchHome
        Write-Host ('launching with CLAUDE_CONFIG_DIR set, for ' + $Seconds + 's ...')
        $proc = Start-Process -FilePath $exePath -PassThru

        # Peak DURING, never only the final count: a final reading of zero is also what
        # "wrote it and cleaned up" produces, so sample while the app is alive.
        $deadline = (Get-Date).AddSeconds($Seconds)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 1
            $now = @((Get-ArtifactSnapshot -Root $ScratchHome).Values | Where-Object { $_ }).Count
            if ($now -gt $peak) { $peak = $now }
            if ($proc.HasExited) {
                Write-Host ('  process exited early, code ' + $proc.ExitCode)
                break
            }
        }

        if (-not $proc.HasExited) {
            $proc.CloseMainWindow() | Out-Null
            Start-Sleep -Seconds 3
            if (-not $proc.HasExited) { $proc.Kill() }
        }
        $proc.WaitForExit(15000) | Out-Null
    }
    finally {
        # Restore in a finally: one leaked value changes every later run in this shell.
        $env:CLAUDE_CONFIG_DIR = $previous
    }

    Start-Sleep -Seconds 2

    $realAfter = Get-ArtifactSnapshot -Root $realHome
    $scratchAfter = Get-ArtifactSnapshot -Root $ScratchHome

    $written = @($script:Artifacts | Where-Object {
            $scratchAfter[$_] -and ($scratchAfter[$_] -ne $scratchBefore[$_]) })
    $touched = @($script:Artifacts | Where-Object { $realAfter[$_] -ne $realBefore[$_] })

    Write-Host ''
    Write-Host ('=' * 72)
    Write-Host ('SCRATCH: ' + $written.Count + '/' + $script:Artifacts.Count +
        ' artifacts written (peak present during run: ' + $peak + ')')
    foreach ($r in $written) { Write-Host ('  + ' + $r) }

    Write-Host ''
    Write-Host ('REAL HOME: ' + $touched.Count + ' ClaudeForge artifact(s) touched')
    foreach ($r in $touched) { Write-Host ('  ! ' + $r) }
    if ($touched.Count -eq 0) {
        Write-Host '  (none -- the copies there are untouched by this run)'
    }
    Write-Host ('=' * 72)

    $used = $written.Count -gt 0
    $untouched = $touched.Count -eq 0
    Write-Host ('scratch home was USED        : ' + $used)
    Write-Host ('real home NOT written by app : ' + $untouched)
    Write-Host ''

    if ($used -and $untouched) {
        Write-Host 'RESULT: PASS'
        $true
    }
    else {
        Write-Host 'RESULT: FAIL'
        $false
    }
}

# Guarded so the VS Code extension's F5, which DOT-SOURCES into a reused host, still runs this --
# an InvocationName check would not, because when dot-sourced the name IS '.'.
if (-not $env:CLAUDEFORGE_RETEST_NOEXEC) {
    $ok = Invoke-ScratchHomeCheck
    if (-not $ok) { throw 'Scratch-home isolation check FAILED -- see the report above.' }
}
