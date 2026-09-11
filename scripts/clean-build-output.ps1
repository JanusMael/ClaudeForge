#Requires -Version 7.0
<#
.SYNOPSIS
    Reclaims disk space by deleting the bin/ and obj/ directories every .csproj in this
    repository builds into — across every git worktree, not just the current one.

.DESCRIPTION
    REPORTS BY DEFAULT AND DELETES NOTHING. Pass -CleanExecute to actually remove anything.
    -WhatIf works on top of that as a second brake.

    ⚠ WHY WORKTREES AND NOT BRANCHES. A branch is a ref; it has no build output. What holds
    build output is a *checkout*, so the complete answer is "every git worktree" — and this
    script asks git for that list rather than walking the filesystem, because a worktree may
    live anywhere on disk, including outside the repository root where a recursive find from
    here would never see it.

    ⛔ BIN/ IS NOT ONLY BUILD OUTPUT — IT HOLDS THE APPS' LOGS. Both apps write their Serilog
    files next to their own executable, so deleting bin/ destroys diagnostic logs that are not
    reproducible and are not in ~/.claude/logs/. This has cost real debugging time before. So
    the report counts the log files it is about to take, and -CleanKeepLogs copies them out
    first. If you are cleaning in order to hand over a bug, run the diagnosis BEFORE this.

    ⚠ AND OBJ/ HOLDS project.assets.json, so the next build needs a restore. That is expected,
    not damage, but it is why the first build afterwards is slow.

.PARAMETER CleanExecute
    Actually delete. Without it this is a report.

.PARAMETER CleanIncludeOrphans
    Also remove bin/obj directories that no longer sit beside a .csproj — left behind when a
    project was renamed or deleted. Reported either way, because they are pure waste and
    nothing else will ever mention them.

.PARAMETER CleanKeepLogs
    Copy any *.log found under bin/ into this directory before deleting.

.PARAMETER CleanRoot
    Override the repository root. Defaults to the script's parent, then the current directory.

.PARAMETER CleanQuiet
    Totals only, no per-directory lines.

.EXAMPLE
    pwsh -NoProfile -File scripts/clean-build-output.ps1
    Report what would be reclaimed. Deletes nothing.

.EXAMPLE
    pwsh -NoProfile -File scripts/clean-build-output.ps1 -CleanExecute -CleanIncludeOrphans

.NOTES
    Set CLEAN_BUILD_OUTPUT_NOEXEC=1 to dot-source this for its functions without running it.

    Output paths are the conventional bin/obj siblings. That is measured, not assumed: no
    csproj, Directory.Build.props or Directory.Build.targets in this repository sets
    BaseOutputPath, BaseIntermediateOutputPath, OutputPath, IntermediateOutputPath or
    UseArtifactsOutput. The script re-checks and WARNS if that ever stops being true, because
    a redirected project would otherwise be silently skipped and its space never reclaimed.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch] $CleanExecute,
    [switch] $CleanIncludeOrphans,
    [string] $CleanKeepLogs,
    [string] $CleanRoot,
    [switch] $CleanQuiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Properties that move a project's output somewhere other than a bin/obj sibling.
$script:RedirectProperties = @(
    'BaseOutputPath', 'BaseIntermediateOutputPath', 'OutputPath',
    'IntermediateOutputPath', 'ArtifactsPath', 'UseArtifactsOutput'
)

# ---------------------------------------------------------------- formatting

function Format-CleanBytes {
    param([long] $Bytes)

    if ($Bytes -ge 1073741824) { return ('{0,8:N2} GiB' -f ($Bytes / 1073741824)) }
    if ($Bytes -ge 1048576) { return ('{0,8:N2} MiB' -f ($Bytes / 1048576)) }
    if ($Bytes -ge 1024) { return ('{0,8:N2} KiB' -f ($Bytes / 1024)) }
    return ('{0,8} B  ' -f $Bytes)
}

# ---------------------------------------------------------------- discovery

function Get-CleanRepoRoot {
    if (-not [string]::IsNullOrWhiteSpace($CleanRoot)) {
        return [System.IO.Path]::GetFullPath($CleanRoot)
    }

    # $PSScriptRoot is empty when dot-sourced from a prompt rather than run as a file.
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    }

    return (Get-Location).Path
}

function Get-CleanWorktreeRoot {
    <#  Every checkout git knows about. Asked of git rather than found on disk: a worktree can
        be created anywhere, and one sitting outside the repository root is exactly the case a
        recursive search misses and a "why is my disk still full" question comes from. #>
    param([string] $RepoRoot)

    $roots = @()
    try {
        Push-Location -LiteralPath $RepoRoot
        try {
            $lines = @(& git worktree list --porcelain 2>&1)
            if ($LASTEXITCODE -eq 0) {
                foreach ($line in $lines) {
                    if ("$line" -match '^worktree\s+(?<p>.+)$') {
                        $roots += [System.IO.Path]::GetFullPath($Matches['p'])
                    }
                }
            }
        }
        finally { Pop-Location }
    }
    catch [System.Management.Automation.CommandNotFoundException] {
        Write-Warning 'git is not on PATH - falling back to the repository root only.'
    }

    if ($roots.Count -eq 0) {
        Write-Warning 'No git worktrees reported - falling back to the repository root only.'
        $roots = @($RepoRoot)
    }

    return $roots
}

function Test-CleanRedirect {
    <#  Warn, never guess. A project that redirects its output would have its real output
        directory silently skipped, and the reported total would quietly understate. #>
    param([string] $ProjectPath)

    $text = Get-Content -LiteralPath $ProjectPath -Raw -ErrorAction SilentlyContinue
    if (-not $text) { return $null }

    foreach ($property in $script:RedirectProperties) {
        if ($text -match "<$property\s*>") { return $property }
    }

    return $null
}

function Test-CleanUnderAny {
    <#  Is $Path inside any of $Roots? #>
    param([string] $Path, [string[]] $Roots)

    foreach ($root in $Roots) {
        $normalized = $root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        if ($Path.StartsWith($normalized, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }

    return $false
}

function Get-CleanProjectTarget {
    <#  The bin/obj siblings of every .csproj under a worktree.

        Projects nested inside a bin/ or obj/ are skipped: those are copies of source dragged
        into an output directory, and treating one as a project root invents targets that do
        not exist.

        ⛔ -ExcludeRoots IS NOT OPTIONAL POLISH. This repository keeps its agent worktrees at
        .claude/worktrees/*, i.e. INSIDE the main worktree, so a recursive scan from the root
        walks straight into them and counts their output as the root's. Measured: the root
        reported 76 projects and 10.96 GiB when it owns 30, and the grand total came to
        17.46 GiB against 14.45 GiB actually on disk. Over-reporting is the harmless half —
        the same paths also entered the delete list twice. #>
    param([string] $WorktreeRoot, [string[]] $ExcludeRoots = @())

    $projects = @(Get-ChildItem -LiteralPath $WorktreeRoot -Recurse -Force -File -Filter '*.csproj' `
            -ErrorAction SilentlyContinue |
        Where-Object {
            $rel = $_.FullName.Substring($WorktreeRoot.Length)
            ($rel -notmatch '[\\/](bin|obj)[\\/]') -and
            -not (Test-CleanUnderAny -Path $_.FullName -Roots $ExcludeRoots)
        })

    $targets = @()
    $redirected = @()

    foreach ($project in $projects) {
        $redirect = Test-CleanRedirect -ProjectPath $project.FullName
        if ($redirect) {
            $redirected += [ordered]@{ Project = $project.FullName; Property = $redirect }
        }

        foreach ($name in @('bin', 'obj')) {
            $candidate = Join-Path $project.DirectoryName $name
            if (Test-Path -LiteralPath $candidate -PathType Container) {
                $targets += $candidate
            }
        }
    }

    return [ordered]@{
        Targets    = @($targets | Sort-Object -Unique)
        Projects   = $projects.Count
        Redirected = $redirected
    }
}

function Get-CleanOrphan {
    <#  bin/ and obj/ directories with no .csproj beside them - left by a renamed or deleted
        project. Nothing else in a build ever mentions these again, so they accumulate. #>
    param([string] $WorktreeRoot, [string[]] $KnownTargets, [string[]] $ExcludeRoots = @())

    $known = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]$KnownTargets, [System.StringComparer]::OrdinalIgnoreCase)

    $all = @(Get-ChildItem -LiteralPath $WorktreeRoot -Recurse -Force -Directory `
            -ErrorAction SilentlyContinue |
        Where-Object {
            ($_.Name -in @('bin', 'obj')) -and
            -not (Test-CleanUnderAny -Path $_.FullName -Roots $ExcludeRoots)
        })

    $orphans = @()
    foreach ($dir in $all) {
        if ($known.Contains($dir.FullName)) { continue }

        # A bin/ inside another bin/ is already covered by deleting the outer one.
        $rel = $dir.FullName.Substring($WorktreeRoot.Length)
        if ($rel -match '[\\/](bin|obj)[\\/].*[\\/](bin|obj)$') { continue }

        $siblings = @(Get-ChildItem -LiteralPath $dir.Parent.FullName -Filter '*.csproj' `
                -File -Force -ErrorAction SilentlyContinue)
        if ($siblings.Count -eq 0) { $orphans += $dir.FullName }
    }

    return $orphans
}

# ---------------------------------------------------------------- measurement

function Measure-CleanDirectory {
    param([string] $Path)

    $files = @(Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue)
    $bytes = 0L
    if ($files.Count -gt 0) {
        $bytes = [long](($files | Measure-Object -Property Length -Sum).Sum)
    }

    # ⛔ The apps log next to their executable. Counting these is the difference between
    # "reclaimed 14 GiB" and "reclaimed 14 GiB and the log you needed".
    $logs = @($files | Where-Object { $_.Extension -eq '.log' })

    return [ordered]@{
        Path  = $Path
        Bytes = $bytes
        Files = $files.Count
        Logs  = $logs
    }
}

# ---------------------------------------------------------------- deletion

function Test-CleanContained {
    <#  Refuse to delete anything that is not genuinely beneath a known worktree. Cheap
        insurance against a junction, a symlink or a mangled path taking the delete somewhere
        it was never pointed. #>
    param([string] $Path, [string[]] $Roots)

    $full = [System.IO.Path]::GetFullPath($Path)
    foreach ($root in $Roots) {
        $normalized = [System.IO.Path]::GetFullPath($root).TrimEnd('\', '/')
        if ($full.StartsWith($normalized + [System.IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Remove-CleanDirectory {
    <#  Remove-Item first; on Windows fall back to the \\?\ form, which is the only thing that
        reaches a path past MAX_PATH - and deep obj/ trees get there easily. Returns $null on
        success or the failure message, so one locked file reports itself instead of aborting
        the whole run (the usual cause is simply that the app is still open). #>
    param([string] $Path)

    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        return $null
    }
    catch [System.IO.IOException], [System.UnauthorizedAccessException],
    [System.IO.PathTooLongException] {
        $first = $_.Exception.Message
        if (-not $IsWindows) { return $first }

        try {
            [System.IO.Directory]::Delete('\\?\' + $Path, $true)
            return $null
        }
        catch [System.IO.IOException], [System.UnauthorizedAccessException] {
            return $first
        }
    }
}

# ---------------------------------------------------------------- main

function Invoke-CleanMain {
    $repoRoot = Get-CleanRepoRoot
    $worktrees = @(Get-CleanWorktreeRoot -RepoRoot $repoRoot)

    Write-Host ''
    Write-Host 'Build-output cleanup' -ForegroundColor Cyan
    Write-Host ('  repository : ' + $repoRoot)
    Write-Host ('  worktrees  : ' + $worktrees.Count)
    if (-not $CleanExecute) {
        Write-Host '  mode       : REPORT ONLY - pass -CleanExecute to delete' -ForegroundColor Yellow
    }
    Write-Host ''

    $measurements = @()
    $allRedirected = @()
    $orphanPaths = @()
    # ⛔ Measured separately but scanned for logs IDENTICALLY. A first version collected logs
    # from project targets only, and missed one sitting in an ORPHANED bin/ — a directory that
    # -CleanIncludeOrphans deletes. The warning would have stayed silent about the very file
    # the flag was about to destroy, which is worse than having no warning at all.
    $orphanMeasurements = @()

    foreach ($worktree in $worktrees) {
        if (-not (Test-Path -LiteralPath $worktree)) {
            Write-Warning ('Worktree is registered but missing on disk: ' + $worktree)
            continue
        }

        # Worktrees nested inside THIS one belong to their own pass, not this one.
        $nested = @($worktrees | Where-Object {
                $_ -ne $worktree -and (Test-CleanUnderAny -Path $_ -Roots @($worktree))
            })

        $found = Get-CleanProjectTarget -WorktreeRoot $worktree -ExcludeRoots $nested
        $allRedirected += $found.Redirected

        $orphans = @()
        if ($CleanIncludeOrphans -or -not $CleanQuiet) {
            $orphans = @(Get-CleanOrphan -WorktreeRoot $worktree -KnownTargets $found.Targets `
                    -ExcludeRoots $nested)
            $orphanPaths += $orphans
            foreach ($orphan in $orphans) {
                $orphanMeasurements += Measure-CleanDirectory -Path $orphan
            }
        }

        $subtotal = 0L
        foreach ($target in $found.Targets) {
            $m = Measure-CleanDirectory -Path $target
            $measurements += $m
            $subtotal += $m.Bytes
        }

        $label = $worktree
        if ($worktree.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
            $trimmed = $worktree.Substring($repoRoot.Length).TrimStart('\', '/')
            if ($trimmed) { $label = '.' + [System.IO.Path]::DirectorySeparatorChar + $trimmed }
            else { $label = '. (this worktree)' }
        }

        Write-Host ('  {0}  {1}  {2} projects, {3} dirs' -f `
            (Format-CleanBytes $subtotal), $label, $found.Projects, $found.Targets.Count)
    }

    $totalBytes = 0L
    foreach ($m in $measurements) { $totalBytes += $m.Bytes }

    $targetLogs = @($measurements | ForEach-Object { $_.Logs } | Where-Object { $_ })
    $orphanLogs = @($orphanMeasurements | ForEach-Object { $_.Logs } | Where-Object { $_ })

    # Orphan logs are only at risk when -CleanIncludeOrphans is passed, but they are counted
    # in the warning either way: "there is a log over there and this flag will take it" is the
    # thing worth saying BEFORE the flag is used, not after.
    $logFiles = @($targetLogs) + @($orphanLogs)

    # ---- warnings before anything is removed ----

    if ($allRedirected.Count -gt 0) {
        Write-Host ''
        Write-Warning ('{0} project(s) REDIRECT their output, so their real output directory is NOT covered here:' -f $allRedirected.Count)
        foreach ($r in $allRedirected) {
            Write-Host ('    ' + $r.Property + ' in ' + $r.Project) -ForegroundColor Yellow
        }
    }

    if ($orphanPaths.Count -gt 0) {
        $orphanBytes = 0L
        foreach ($om in $orphanMeasurements) { $orphanBytes += $om.Bytes }
        Write-Host ''
        $verb = if ($CleanIncludeOrphans) { 'INCLUDED' } else { 'NOT included - pass -CleanIncludeOrphans' }
        Write-Host ('  {0}  in {1} orphaned bin/obj with no .csproj beside them  [{2}]' -f `
            (Format-CleanBytes $orphanBytes), $orphanPaths.Count, $verb) -ForegroundColor Yellow
        if ($CleanIncludeOrphans) { $totalBytes += $orphanBytes }
    }

    if ($logFiles.Count -gt 0) {
        Write-Host ''
        # ASCII, not an emoji: console encoding is not guaranteed to be UTF-8 and a mangled
        # glyph in a warning about irreversible deletion is the worst place to find that out.
        Write-Host ('  [!] {0} application .log file(s) live under these bin/ directories.' -f $logFiles.Count) -ForegroundColor Red
        Write-Host '     Both apps log beside their own executable, and those logs are not' -ForegroundColor Red
        Write-Host '     reproducible. Use -CleanKeepLogs <dir> to copy them out first.' -ForegroundColor Red
        if ($orphanLogs.Count -gt 0) {
            Write-Host ('     {0} of them is in an ORPHANED bin/ - taken only by -CleanIncludeOrphans.' -f $orphanLogs.Count) -ForegroundColor Red
        }
        foreach ($log in $logFiles) {
            Write-Host ('       ' + $log.FullName) -ForegroundColor DarkYellow
        }
    }

    Write-Host ''
    Write-Host ('  TOTAL RECLAIMABLE: ' + (Format-CleanBytes $totalBytes)) -ForegroundColor Green
    Write-Host ''

    if (-not $CleanExecute) {
        Write-Host 'Nothing was deleted. Re-run with -CleanExecute to reclaim it.' -ForegroundColor Yellow
        return
    }

    # ---- rescue logs ----

    if ($logFiles.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($CleanKeepLogs)) {
        $dest = [System.IO.Path]::GetFullPath($CleanKeepLogs)
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
        foreach ($log in $logFiles) {
            # Name-collide across worktrees, so prefix with a slug of the directory.
            $slug = ($log.DirectoryName -replace '[^A-Za-z0-9]+', '-').Trim('-')
            if ($slug.Length -gt 60) { $slug = $slug.Substring($slug.Length - 60) }
            Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $dest ($slug + '_' + $log.Name)) -Force
        }
        Write-Host ('Copied {0} log file(s) to {1}' -f $logFiles.Count, $dest) -ForegroundColor Cyan
    }

    # ---- delete ----

    $toRemove = @($measurements | ForEach-Object { $_.Path })
    if ($CleanIncludeOrphans) { $toRemove += $orphanPaths }

    $removed = 0L
    $failures = @()

    foreach ($path in @($toRemove | Sort-Object -Unique)) {
        if (-not (Test-CleanContained -Path $path -Roots $worktrees)) {
            $failures += ($path + ' :: refused, outside every known worktree')
            continue
        }

        # $script:CleanCmdlet, not $PSCmdlet: this is a plain function, and under StrictMode
        # reaching for an automatic variable that only exists on the script scope is a throw
        # waiting for the one machine where the scope chain differs.
        if (-not $script:CleanCmdlet.ShouldProcess($path, 'Delete recursively')) { continue }

        $before = (Measure-CleanDirectory -Path $path).Bytes
        # NOT $error - that is PowerShell's automatic error-history array.
        $removeError = Remove-CleanDirectory -Path $path
        if ($removeError) {
            $failures += ($path + ' :: ' + $removeError)
        }
        else {
            $removed += $before
            if (-not $CleanQuiet) { Write-Host ('  removed ' + $path) -ForegroundColor DarkGray }
        }
    }

    Write-Host ''
    Write-Host ('  RECLAIMED: ' + (Format-CleanBytes $removed)) -ForegroundColor Green

    if ($failures.Count -gt 0) {
        Write-Host ''
        Write-Warning ('{0} director(ies) could not be removed - usually an app or IDE still holding a file:' -f $failures.Count)
        foreach ($f in $failures) { Write-Host ('    ' + $f) -ForegroundColor Yellow }
    }

    Write-Host ''
    Write-Host 'obj/ held project.assets.json, so the next build restores first. That is expected.' -ForegroundColor DarkGray
    Write-Host ''
}

# Guarded on an env var, not on $MyInvocation.InvocationName: the VS Code extension
# dot-sources on F5, where InvocationName IS '.', so the conventional guard never runs Main.
if (-not $env:CLEAN_BUILD_OUTPUT_NOEXEC) {
    $script:CleanCmdlet = $PSCmdlet
    Invoke-CleanMain
}
