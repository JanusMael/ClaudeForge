# package-canary.ps1 — prove the repo still works when the shared libraries arrive as PACKAGES.
#
# WHY THIS EXISTS
# ---------------
# Eleven projects under src/ are published as private NuGet packages (plans/00001). Everything
# else — the app, its two product-specific libraries, the sample and all nine test projects
# — consumes them one of two ways:
#
#     UseSharedPackages unset/false  ->  ProjectReference   (development: the editor, the suite)
#     UseSharedPackages=true         ->  PackageReference   (this canary, and the release)
#
# Development mode is what everyone runs, so the package mode is the one that rots unobserved.
# This script is the observation: pack at a throwaway version, restore into a throwaway package
# cache, then build, test and publish against the packages rather than the projects.
#
# USAGE
# -----
#     pwsh -NoProfile -File scripts/package-canary.ps1
#     pwsh -NoProfile -File scripts/package-canary.ps1 -PackOnly
#     pwsh -NoProfile -File scripts/package-canary.ps1 -CanaryVersion 1.2.3-rc1
#     pwsh -NoProfile -File scripts/package-canary.ps1 -SkipPublish   # faster inner loop
#
# Requires PowerShell 7+.
#
# ----------------------------------------------------------------------------
# THREE THINGS THAT ARE NOT OBVIOUS
# ----------------------------------------------------------------------------
# 1. ⛔ THE VERSION MUST BE UNIQUE PER RUN, AND A UNIQUE VERSION IS NOT ENOUGH ON ITS OWN.
#    NuGet extracts a package into the global cache keyed by id+version and will NOT re-extract.
#    Pack the same version twice and the second restore silently reuses the first extraction —
#    the canary then validates yesterday's build and reports success. The default version is
#    therefore timestamped to the second, AND the restore is pointed at a NUGET_PACKAGES
#    directory named after that version, so it is empty by construction. Neither alone is
#    sufficient: the version guards CI, the isolated directory guards a developer running this
#    twice inside one minute.
#
# 2. ⚠ PACKING HAPPENS IN DEVELOPMENT MODE, DELIBERATELY. The eleven reference each other by
#    project in both modes, so `dotnet pack` must NOT be given -p:UseSharedPackages=true — that
#    would ask them to consume packages of themselves that do not exist yet. Only the CONSUMING
#    half switches.
#
# 3. ⚠ AN ISOLATED NUGET_PACKAGES RE-DOWNLOADS EVERYTHING — Avalonia, Serilog, MSTest, the lot.
#    The first run is slow and needs the network. That is the cost of the guarantee in (1); do
#    not "optimise" it by pointing at the real cache.

[CmdletBinding()]
param(
    # Named CanaryVersion rather than Version: param() overwrites the caller's variables with its
    # own defaults when this file is dot-sourced, and $Version is a name a host is likely to hold.
    [string] $CanaryVersion,
    [string] $FeedDirectory,
    [string] $PackageCacheDirectory,
    # Defaults to the host's own RID, so this runs unchanged on a Windows desktop and a Linux
    # CI agent. Publishing is part of the canary rather than an extra: trimming and asset
    # resolution are exactly the things a package can break.
    [string] $RuntimeIdentifier,
    [switch] $PackOnly,
    [switch] $SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

function New-CanaryVersion {
    # Sortable, obviously-throwaway, and a valid SemVer prerelease. The seconds component is
    # what makes two runs in one minute distinguishable.
    return '0.0.0-local-' + (Get-Date -Format 'yyyyMMddHHmmss')
}

function Invoke-Dotnet {
    param([string[]] $DotnetArgs, [string] $What)

    Write-Host ''
    Write-Host ('==> dotnet ' + ($DotnetArgs -join ' ')) -ForegroundColor Cyan
    & dotnet @DotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw ($What + ' failed with exit code ' + $LASTEXITCODE + '.')
    }
}

function Main {
    $repoRoot = Get-RepoRoot
    $solution = Join-Path $repoRoot 'ClaudeForge.slnx'

    $version = if ($CanaryVersion) { $CanaryVersion } else { New-CanaryVersion }
    $feed = if ($FeedDirectory) { $FeedDirectory } else { Join-Path $repoRoot 'artifacts/localfeed' }
    # ⚠ A FRESH DIRECTORY PER RUN, rather than one directory emptied each time. Deleting it is
    # not reliably possible on Windows: MSBuild's persistent build nodes keep
    # Avalonia.Build.Tasks.dll open out of the package cache, and Remove-Item fails with
    # "Access to the path ... is denied" AFTER the packing work is already done. Naming the
    # directory for the version gets the same isolation with nothing to unlock.
    $cache = if ($PackageCacheDirectory) {
        $PackageCacheDirectory
    }
    else {
        Join-Path $repoRoot (Join-Path 'artifacts/canary-packages' $version)
    }

    Write-Host ''
    Write-Host ('Canary version : ' + $version)
    Write-Host ('Local feed     : ' + $feed)
    Write-Host ('Package cache  : ' + $cache)

    # MSBuild's persistent nodes hold DLLs open out of both the feed and the package cache;
    # shutting them down first is what makes the cleanup below succeed on Windows rather than
    # fail with "Access to the path ... is denied" once the real work is already done.
    Write-Host ''
    Write-Host '==> dotnet build-server shutdown' -ForegroundColor Cyan
    & dotnet build-server shutdown | Out-Null

    # A stale package of the SAME id at a DIFFERENT version is harmless, but a feed that grows
    # without bound makes "which one did it restore" unanswerable. Start clean.
    if (Test-Path $feed) { Remove-Item -Path $feed -Recurse -Force }
    New-Item -ItemType Directory -Path $feed -Force | Out-Null

    # Best effort, and deliberately non-fatal: a previous run's cache may still be locked, and
    # failing the canary over housekeeping would be the tail wagging the dog.
    $cacheRoot = Join-Path $repoRoot 'artifacts/canary-packages'
    if (Test-Path $cacheRoot) {
        foreach ($old in @(Get-ChildItem -Path $cacheRoot -Directory -ErrorAction SilentlyContinue)) {
            Remove-Item -Path $old.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # Development mode on purpose — see note 2 in the header.
    Invoke-Dotnet -What 'pack' -DotnetArgs @(
        'pack', $solution,
        '-c', 'Release',
        '-o', $feed,
        # ⚠ Interpolated, NOT concatenated. Inside an @(...) literal the comma binds tighter
        # than +, so '-p:PackageVersion=' + $version arrives as TWO arguments and MSBuild
        # reports "Only one project can be specified" — which names the wrong problem entirely.
        "-p:PackageVersion=$version",
        '--nologo'
    )

    $produced = @(Get-ChildItem -Path $feed -Filter '*.nupkg' -File)
    Write-Host ''
    Write-Host ($produced.Count.ToString() + ' package(s) in the local feed:')
    foreach ($p in $produced) { Write-Host ('  ' + $p.Name) }

    if ($produced.Count -eq 0) {
        throw 'pack produced no packages. Check that the shared projects still state <IsPackable>true</IsPackable>.'
    }

    if ($PackOnly) {
        Write-Host ''
        Write-Host ('PackOnly: stopping here. Consume it with -p:UseSharedPackages=true -p:SharedPackageVersion=' + $version)
        return
    }

    $previousPackages = $env:NUGET_PACKAGES
    try {
        # See note 1: the isolated cache is half the guarantee, not a convenience.
        New-Item -ItemType Directory -Path $cache -Force | Out-Null
        $env:NUGET_PACKAGES = $cache

        $switch = @(
            '-p:UseSharedPackages=true',
            "-p:SharedPackageVersion=$version"   # interpolated for the reason noted above
        )

        # ⚠ `build` restores for itself; a separate `restore` followed by `--no-restore` does
        # NOT work here, and the failure looks like a packaging problem when it is not.
        # Both apps declare <RuntimeIdentifiers>, and a plain solution restore writes no
        # RID-specific target into project.assets.json, so the build fails with NETSDK1047
        # "doesn't have a target for net10.0/win-x64". Reproduced in DEVELOPMENT mode too,
        # which is how it was ruled out as a symptom of the switch.
        Invoke-Dotnet -What 'build' -DotnetArgs (@('build', $solution, '-c', 'Release', '--nologo') + $switch)
        Invoke-Dotnet -What 'test'  -DotnetArgs (@('test', $solution, '-c', 'Release', '--no-build', '--nologo') + $switch)

        if (-not $SkipPublish) {
            $rid = if ($RuntimeIdentifier) {
                $RuntimeIdentifier
            }
            else {
                [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
            }

            Write-Host ''
            Write-Host ('Publishing the app for ' + $rid)

            # TWO-APP GUARD NARROWED - plans/00003 Phase 0. OpenCodeForge's project directory was
            # in this list; restore it when OpenCodeForge rejoins this branch. (Named without its
            # repo-relative prefix on purpose: BuildFilePathIntegrityTests scans this script and
            # reads that spelling as a claim the directory exists.)
            foreach ($app in @('src/ClaudeForge')) {
                Invoke-Dotnet -What "publish $app" -DotnetArgs (@(
                    'publish', (Join-Path $repoRoot $app),
                    '-c', 'Release',
                    '-r', $rid,
                    '--self-contained', 'true',
                    '--nologo'
                ) + $switch)
            }
        }

        Write-Host ''
        Write-Host ('Package canary PASSED at ' + $version) -ForegroundColor Green
    }
    finally {
        # One leaked NUGET_PACKAGES silently redirects every later restore in this session.
        if ($null -eq $previousPackages) {
            Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
        }
        else {
            $env:NUGET_PACKAGES = $previousPackages
        }
    }
}

# Guarded on an env var, not on $MyInvocation.InvocationName: the VS Code extension dot-sources
# on F5, where InvocationName IS '.', so the conventional guard would never run Main.
if (-not $env:PACKAGE_CANARY_NOEXEC) {
    Main
}
