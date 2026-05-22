<#
.SYNOPSIS
    Multi-RID publish orchestrator for ClaudeForge.

.DESCRIPTION
    Drives Publish-Rid.ps1 across every supported RID, prompting the user
    before each one so a developer can cherry-pick the architectures they
    actually need (or quit partway through). Pass -All to skip prompts and
    build every RID unattended — this is the CI / release-cut mode.

    Script layout (all scripts live under src/publish/):

        publish.ps1                   (THIS FILE — orchestrator)
            └── Publish-Rid.ps1       (single-RID worker)
                ▲
        publish-<rid>.ps1             (thin per-RID wrappers that call
                                       Publish-Rid.ps1 directly)

    The orchestrator:
      1. Wipes dist/ and bin/obj/ once up front so all RIDs land on a
         clean slate. Per-RID scripts DO NOT need to clean again.
      2. For each RID, prompts `[Y/n/a/q]` (unless -All or -Rids explicitly
         narrows the set). Default on Enter is Yes.
      3. Invokes Publish-Rid.ps1 WITHOUT -Clean (orchestrator already did).
      4. Aggregates warning counts and runs Analyze-XamlClosures.ps1 if any
         ILLink warnings surfaced — the closure analyzer is metadata-only
         and safe to run unconditionally.

    Run from any directory:
        pwsh src/publish/publish.ps1
        pwsh src/publish/publish.ps1 -All
        pwsh src/publish/publish.ps1 -Rids win-x64,win-arm64

.PARAMETER All
    Build every RID in $Rids without prompting. Typical CI usage:
      `pwsh src/publish/publish.ps1 -All`

.PARAMETER Rids
    Restrict the orchestration to this subset of RIDs. Still prompts per
    RID unless -All is also specified. Defaults to all six supported RIDs.

.EXAMPLE
    # Interactive: step through all six RIDs, answering [Y/n/a/q] for each.
    pwsh src/publish/publish.ps1

.EXAMPLE
    # Unattended: build every RID, no prompts. Release-cut / CI mode.
    pwsh src/publish/publish.ps1 -All

.EXAMPLE
    # Interactive, but only offer the two Windows RIDs.
    pwsh src/publish/publish.ps1 -Rids win-x64,win-arm64
#>

[CmdletBinding()]
param(
    [switch] $All,

    [ValidateSet("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string[]] $Rids = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
)

$ErrorActionPreference = 'Stop'

# Suppress the built-in cmdlet progress UI ("Removing…" / "Searching…") that
# Remove-Item -Recurse and Get-ChildItem -Recurse emit when walking a large
# bin/obj tree. The progress bar uses VT escape sequences that IDE output
# panes (Rider, VS Code's Output window) don't render — they overwrite or
# eclipse the dotnet publish lines we actually want to read. Keeping it
# silent lets the build log scroll naturally in any host.
$ProgressPreference = 'SilentlyContinue'

# $PSScriptRoot is src/publish/ — the src/ root is one level up.
$srcRoot         = Split-Path $PSScriptRoot -Parent
$distFolder      = Join-Path $srcRoot "dist"
$logFolder       = Join-Path $distFolder "logs"
# All helper scripts live alongside this script in the publish/ directory.
$worker          = Join-Path $PSScriptRoot "Publish-Rid.ps1"
$closureAnalyzer = Join-Path $PSScriptRoot "Analyze-XamlClosures.ps1"

Set-Location $srcRoot

# ── 0. Workload preflight (Windows RIDs only) ────────────────────────────────
# LayeredEditors.Avalonia.Services.csproj sets <UseMauiEssentials>true</UseMauiEssentials>
# for its net10.0-windows10.0.19041.0 TFM so DefaultShareService can call the
# native Windows share flyout via Microsoft.Maui.Essentials. That property
# triggers an SDK workload check (NETSDK1147) which fails on any machine that
# does not have the `maui-windows` workload installed — typically every fresh
# clone of this repo on a machine without Visual Studio 2022+.
#
# Why this is opt-in / surgical rather than a blanket `dotnet workload restore`:
#   - `restore` refreshes ALL advertising manifests on every invocation
#     (slow: 10-30 s) and tries to reconcile every installed workload on the
#     machine to its latest manifest version — including unrelated workloads
#     (android, ios, etc.) that this project never touches.
#   - This preflight is a no-op on the fast path (one `dotnet workload list`
#     parse, ~500 ms) and only triggers an install if `maui-windows` is
#     genuinely absent. `--skip-manifest-update` keeps the install targeted.
#
# Gating: only runs when at least one Windows RID is in the build set. Linux /
# macOS RIDs publish via the net10.0 TFM which doesn't reference Maui, so
# they don't need the workload — running the preflight then would just be
# pointless overhead.
#
# Failure path: if the workload install fails (typically: not running
# elevated; `dotnet workload install` writes to %ProgramFiles%\dotnet\
# sdk-manifests on Windows and requires Admin), we abort here with a clear
# remediation hint rather than letting the per-RID publish fail later with
# the less-obvious NETSDK1147.
$buildingWindows = ($Rids | Where-Object { $_ -like 'win-*' }).Count -gt 0
if ($buildingWindows)
{
    Write-Host "Checking required .NET workloads for Windows publishes..." -ForegroundColor Magenta
    # `dotnet workload list` writes a tabular display to stdout. Lines after
    # the column header start with the workload id (one per row). Use a
    # multiline regex anchored to start-of-line (with optional leading
    # whitespace) and a word boundary so we don't false-positive on
    # hypothetical future ids like `maui-windows-foo`.
    $workloadOutput = & dotnet workload list 2>&1 | Out-String
    if ($workloadOutput -notmatch '(?m)^\s*maui-windows\b')
    {
        Write-Host "  Missing workload: maui-windows. Installing..." -ForegroundColor Yellow
        dotnet workload install maui-windows --skip-manifest-update
        $workloadExit = $LASTEXITCODE
        if ($workloadExit -ne 0)
        {
            Write-Host "" -ForegroundColor Red
            Write-Host "Workload install failed (exit $workloadExit)." -ForegroundColor Red
            Write-Host "Remediation:" -ForegroundColor Yellow
            Write-Host "  - On Windows, run this script from an elevated (Admin) shell, OR" -ForegroundColor Yellow
            Write-Host "  - Install manually: dotnet workload install maui-windows" -ForegroundColor Yellow
            Write-Host "  - Verify with:      dotnet workload list" -ForegroundColor Yellow
            Write-Host "" -ForegroundColor Red
            exit $workloadExit
        }
        Write-Host "  Workload 'maui-windows' installed." -ForegroundColor Green
    }
    else
    {
        Write-Host "  Workload 'maui-windows' already installed." -ForegroundColor DarkGreen
    }
}

# ── 1. Global pre-clean ─────────────────────────────────────────────────────
# Wipe dist/ (all prior zips, logs, staging) and every bin/obj under src/.
#
# We use a manual bin/obj wipe rather than `dotnet clean` because the Release
# config sets <SelfContained>true</SelfContained>, which makes the SDK
# auto-inject the host RID (typically win-x64) into the clean-time restore
# graph evaluation. If the assets file left over from the previous run's
# LAST-published RID (e.g. osx-arm64) does not contain a net10.0/<host-rid>
# target — which it won't, because each `dotnet publish -r <rid>` rewrites
# project.assets.json for just that RID — then `dotnet clean` fails with
# NETSDK1047. A manual wipe sidesteps that chicken-and-egg entirely.
Write-Host "Cleaning previous build artifacts..." -ForegroundColor Magenta
if (Test-Path $distFolder) { Remove-Item -Recurse -Force $distFolder }
New-Item -ItemType Directory -Path $distFolder -Force | Out-Null
New-Item -ItemType Directory -Path $logFolder  -Force | Out-Null

Write-Host "  Wiping bin/ and obj/ across the src tree..." -ForegroundColor DarkGray
$cleanTargets = Get-ChildItem -Path $srcRoot -Directory -Recurse -Force `
        -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' } |
    Select-Object -ExpandProperty FullName
foreach ($dir in $cleanTargets) {
    Remove-Item -Recurse -Force -LiteralPath $dir -ErrorAction SilentlyContinue
}

# ── 2. Per-RID loop with optional prompting ─────────────────────────────────
# `Read-BuildRidChoice` returns one of:
#   'Yes'  — build this RID, keep prompting for the rest
#   'No'   — skip this RID, keep prompting
#   'All'  — build this RID and every remaining one without prompting
#   'Quit' — stop the loop now (nothing more will be built)
#
# The -All switch short-circuits the prompt by pre-setting $buildAllRemaining.
function Read-BuildRidChoice {
    param([string] $Rid)
    while ($true) {
        # `Write-Host -NoNewline` + `Read-Host` produces an inline prompt that
        # plays nicely with PowerShell transcripts and CI log capture.
        Write-Host ("Build {0}? [Y]es / [N]o / [A]ll remaining / [Q]uit: " -f $Rid) `
            -ForegroundColor Cyan -NoNewline
        $answer = (Read-Host).Trim().ToLowerInvariant()
        switch ($answer) {
            ''  { return 'Yes'  }   # Enter = default Yes
            'y' { return 'Yes'  }
            'yes' { return 'Yes' }
            'n' { return 'No'   }
            'no' { return 'No' }
            'a' { return 'All'  }
            'all' { return 'All' }
            'q' { return 'Quit' }
            'quit' { return 'Quit' }
            default {
                Write-Host "  Unknown response '$answer' — please enter Y/N/A/Q." -ForegroundColor Yellow
            }
        }
    }
}

$buildAllRemaining = [bool] $All
$results           = [System.Collections.Generic.List[object]]::new()
$abort             = $false

foreach ($rid in $Rids) {
    if ($abort) { break }

    # Decide build/skip/abort for this RID. Note: `continue`/`break` inside a
    # `switch` block act on the SWITCH, not the enclosing foreach — so we
    # translate the choice into a local action variable and act on it OUTSIDE
    # the switch. This is the idiomatic PowerShell control-flow pattern for
    # foreach-containing-switch.
    $action = 'Build'
    if (-not $buildAllRemaining) {
        switch (Read-BuildRidChoice -Rid $rid) {
            'Yes'  { $action = 'Build' }
            'No'   { $action = 'Skip'  }
            'All'  { $action = 'Build'; $buildAllRemaining = $true }
            'Quit' { $action = 'Abort' }
        }
    }

    if ($action -eq 'Skip') {
        Write-Host "  Skipping $rid." -ForegroundColor DarkGray
        continue
    }
    if ($action -eq 'Abort') {
        Write-Host "  Aborting remaining RIDs." -ForegroundColor DarkGray
        $abort = $true
        continue
    }

    # Delegate to the worker. -Clean is NOT passed — the orchestrator has
    # already wiped bin/obj/dist once for this whole run, and cleaning again
    # per RID would discard the previous RID's zip (which Publish-Rid's
    # selective clean would NOT do, but the full wipe the orchestrator does
    # WOULD). Call the worker with -DistFolder so sibling logs and zips all
    # land in the same orchestrator-owned folder.
    $result = & $worker -Rid $rid -DistFolder $distFolder
    $results.Add($result)
}

# ── 3. Post-process: closure analyzer if any RID produced IL warnings ───────
$ridsWithWarnings = @($results | Where-Object { $_.WarningCount -gt 0 } | ForEach-Object { $_.Rid })

if ($ridsWithWarnings.Count -gt 0) {
    Write-Host "`n=========================================================" -ForegroundColor Yellow
    Write-Host ("ILLink warnings detected in: {0}" -f ($ridsWithWarnings -join ', ')) -ForegroundColor Yellow
    Write-Host "Running Analyze-XamlClosures.ps1 to map XamlClosure_N -> source XAML..." -ForegroundColor Yellow
    Write-Host "=========================================================" -ForegroundColor Yellow

    if (-not (Test-Path $closureAnalyzer)) {
        Write-Host "Analyze-XamlClosures.ps1 not found at $closureAnalyzer -- skipping analysis." -ForegroundColor Red
    }
    else {
        # Pick the first affected RID's log + linked output. Trim warnings are
        # almost always RID-invariant (the same Semi.Avalonia closures appear
        # across all platforms), so one log is representative.
        $primaryRid = $ridsWithWarnings[0]
        $primaryLog = Join-Path $logFolder "publish-$primaryRid.log"
        $linkedPath = Join-Path $srcRoot `
            ("ClaudeForge/obj/Release/net10.0/{0}/linked/ClaudeForge.dll" -f $primaryRid)

        # Quick confirmation diagnostic per TRIMMING.md step 1: did the
        # suppression XML actually reach ILLink?
        $linkAttrHits = Select-String -Path $primaryLog -Pattern '--link-attributes' -ErrorAction SilentlyContinue
        if ($linkAttrHits) {
            Write-Host ("[check] ILLink received {0} --link-attributes arg(s) — suppression XML did reach the linker." -f $linkAttrHits.Count) -ForegroundColor Gray
        }
        else {
            Write-Host "[check] NO --link-attributes in ILLink command line — the csproj wiring is broken. See TRIMMING.md (the four-row comparison table)." -ForegroundColor Red
        }

        if (Test-Path $linkedPath) {
            & $closureAnalyzer -Path $linkedPath -WarningsPath $primaryLog -IncludeReferences
        }
        else {
            # Fallback: let the analyzer auto-discover a linked assembly (it
            # defaults to win-x64; log-vs-RID mismatch is rare but harmless).
            Write-Host ("Linked assembly not found at {0}; falling back to analyzer defaults." -f $linkedPath) -ForegroundColor DarkYellow
            & $closureAnalyzer -WarningsPath $primaryLog -IncludeReferences
        }
    }

    Write-Host "`nWARNING: publish completed with ILLink warnings — see TRIMMING.md for the suppression workflow." -ForegroundColor Yellow
}
elseif ($results.Count -gt 0) {
    Write-Host "`nNo ILLink warnings detected in any built RID." -ForegroundColor Green
}

# ── 4. Summary table ────────────────────────────────────────────────────────
# Always print a compact summary so the user can see at a glance which RIDs
# built, which failed, and whether anything was skipped via the prompt.
if ($results.Count -gt 0) {
    Write-Host "`nResults:" -ForegroundColor Green
    $results | Format-Table Rid, ExitCode, WarningCount, @{
        Name       = 'Archive'
        Expression = { if ($_.ArchivePath) { Split-Path $_.ArchivePath -Leaf } else { '(none)' } }
    } | Out-Host
    Write-Host "Output zips: $distFolder" -ForegroundColor Green
    Write-Host "Publish logs: $logFolder" -ForegroundColor Gray
    Write-Host "Publish Complete!" -ForegroundColor Green
}
else {
    Write-Host "`nNo RIDs were built." -ForegroundColor DarkYellow
}
