#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Prove the app restores the six shared packages FROM THE PUBLISHED FEED.

.DESCRIPTION
    plans/00003, Phase D. D1 asks for evidence that the release consumes the packages on
    GitHub Packages rather than a locally-packed copy, and D2 asks for a real run proving
    `packages: read` is sufficient. This script is both, and it exists because neither claim
    is observable in the built artifact.

    ⛔ THERE IS NO ARTIFACT-LEVEL TELL. `Directory.Build.targets` sets
    CopyDocumentationFilesFromPackages in package mode specifically to erase the one known
    difference between a PackageReference and a ProjectReference output, and the publish strip
    removes *.xml either way. So a build cannot be inspected after the fact to learn which mode
    produced it — the provenance has to be read at restore time, from NuGet's own record.

    ⭐ WHAT IS ACTUALLY CHECKED. NuGet writes `.nupkg.metadata` beside every extracted package
    in the global packages folder, carrying {"version":…,"contentHash":…,"source":…}. `source`
    is the only field that separates the feed from a folder — and to NuGet a folder source is
    exactly as valid as a remote one, so "the restore succeeded" proves nothing on its own.

    ⛔⛔ THIS WAS NOT A HYPOTHETICAL. On 2026-09-19 a restore at the pinned version reported
    success with all eleven resolving from `artifacts/localfeed` — locally-packed bytes from an
    earlier commit. The folder no longer even held that version; it had earlier, and NuGet's
    global cache is keyed id+version and NEVER re-extracts, so the stale copy was reused
    silently. That is why this script purges before it restores: an absent folder is not enough.

.PARAMETER Project
    The project to restore. Defaults to the app, which is what the release publishes.

.PARAMETER ExpectedSource
    Substring every package's recorded source must contain.

.EXAMPLE
    pwsh -NoProfile -File scripts/verify-feed-restore.ps1
#>
[CmdletBinding()]
param(
    [string] $Project = 'src/ClaudeForge',
    [string] $ExpectedSource = 'nuget.pkg.github.com'
)

$ErrorActionPreference = 'Stop'

# The six. Listed rather than discovered, deliberately: discovery that finds five and reports
# five green is the failure this file is guarding against.
# LIBRARY GUARD NARROWED -- plans/00005. The five bennewitz.ninja.layerededitors.* ids were here;
# the app now takes that code from the ScopedEditors and AppServices packages on nuget.org,
# which this feed does not serve.
$expectedIds = @(
    'bennewitz.ninja.agentforge.abstractions'
    'bennewitz.ninja.agentforge.artifacts'
    'bennewitz.ninja.agentforge.avalonia.shell'
    'bennewitz.ninja.agentforge.core'
    'bennewitz.ninja.agentforge.sdk'
    'bennewitz.ninja.jsonc'
)

function Get-PinnedVersion {
    $props = Join-Path $PSScriptRoot '..' 'Directory.Build.props'
    $text = Get-Content -Path $props -Raw
    if ($text -match '<SharedPackageVersion[^>]*>([^<]+)</SharedPackageVersion>') {
        return $Matches[1].Trim()
    }

    throw 'Could not read SharedPackageVersion from the root Directory.Build.props.'
}

$version = Get-PinnedVersion
Write-Host ('Pinned SharedPackageVersion : ' + $version)
Write-Host ('Expecting source to contain : ' + $ExpectedSource)

# ⛔ A CONFIGURED LOCAL SOURCE THAT DOES NOT EXIST IS A HARD ERROR, NOT A SKIPPED ONE.
# nuget.config maps these six ids to both `localfeed` and `github`. On a fresh checkout
# artifacts/ is gitignored, so the folder is absent and the restore dies with
#   NU1301: The local source '.../artifacts/localfeed' doesn't exist.
# before it ever reaches the feed. ⓘ Other jobs never see this: in development mode the source
# mapping means these ids are never requested at all, and the package canary creates the folder
# by packing into it. Measured on this job's first CI run.
$localFeed = Join-Path $PSScriptRoot '..' 'artifacts' 'localfeed'
if (-not (Test-Path $localFeed)) {
    New-Item -ItemType Directory -Path $localFeed -Force | Out-Null
    Write-Host ('Created empty local feed      : ' + $localFeed)
}

# ⭐ And now the emptiness is EVIDENCE rather than an accident. The folder is allowed to hold
# other versions — a developer's canary run leaves throwaway 0.0.0-local-* packages there — but
# it must not be able to answer for the pinned version, or "resolved from the feed" would be a
# coin toss decided by source ordering.
$localCandidates = @(Get-ChildItem -Path $localFeed -Filter ('*' + $version + '.nupkg') -ErrorAction SilentlyContinue)
if ($localCandidates.Count -gt 0) {
    throw ('artifacts/localfeed holds ' + $localCandidates.Count + ' package(s) at ' + $version +
        '. This check cannot distinguish the feed from the folder while that is true. Remove them ' +
        'and re-run: ' + ($localCandidates.Name -join ', '))
}

$packagesRoot = $env:NUGET_PACKAGES
if ([string]::IsNullOrWhiteSpace($packagesRoot)) {
    $home_ = if ($env:HOME) { $env:HOME } else { $env:USERPROFILE }
    $packagesRoot = Join-Path $home_ '.nuget' 'packages'
}

# ⛔ Purge before restoring. Absence of artifacts/localfeed is NOT enough: the global cache
# keeps whatever a previous restore extracted, keyed id+version, and never re-extracts.
$purged = 0
foreach ($id in $expectedIds) {
    $dir = Join-Path $packagesRoot $id $version
    if (Test-Path $dir) {
        Remove-Item -Recurse -Force $dir
        $purged++
    }
}
Write-Host ('Purged from the global cache : ' + $purged)

Write-Host ''
Write-Host ('==> dotnet restore ' + $Project + ' -p:UseSharedPackages=true')
& dotnet restore $Project -p:UseSharedPackages=true --nologo
if ($LASTEXITCODE -ne 0) {
    throw ('restore failed with exit code ' + $LASTEXITCODE +
        '. A 401 here means the feed credential is missing — see the job that calls this.')
}

$rows = [System.Collections.Generic.List[object]]::new()
foreach ($id in $expectedIds) {
    $metadata = Join-Path $packagesRoot $id $version '.nupkg.metadata'
    if (-not (Test-Path $metadata)) {
        $rows.Add([pscustomobject]@{ Id = $id; Source = '(no .nupkg.metadata)'; Ok = $false })
        continue
    }

    # ⚠ Cast, never [datetime]::Parse-style re-reading: ConvertFrom-Json already types this.
    $source = (Get-Content -Path $metadata -Raw | ConvertFrom-Json).source
    $ok = $source -and $source.Contains($ExpectedSource)
    $rows.Add([pscustomobject]@{ Id = $id; Source = $source; Ok = $ok })
}

Write-Host ''
$rows | ForEach-Object {
    $mark = if ($_.Ok) { 'OK  ' } else { 'FAIL' }
    Write-Host ($mark + '  ' + $_.Id.PadRight(52) + $_.Source)
}

$resolved = @($rows | Where-Object { $_.Ok })
$bad = @($rows | Where-Object { -not $_.Ok })

Write-Host ''
Write-Host ('Packages checked : ' + $rows.Count)
Write-Host ('From the feed    : ' + $resolved.Count)

# Premise, asserted rather than assumed: a run that checked nothing must not report success.
if ($rows.Count -ne $expectedIds.Count) {
    throw ('Expected ' + $expectedIds.Count + ' packages, saw ' + $rows.Count +
        '. This check is vacuous unless every one is accounted for.')
}

if ($bad.Count -gt 0) {
    $detail = ($bad | ForEach-Object { $_.Id + ' <- ' + $_.Source }) -join "`n  "
    throw ("These packages did NOT come from the published feed:`n  " + $detail +
        "`n`nA folder source is exactly as valid to NuGet as a remote one, so the restore " +
        'succeeding proves nothing on its own. Check that artifacts/localfeed is absent and ' +
        'that the global cache was purged.')
}

Write-Host ''
Write-Host ('FEED RESTORE VERIFIED — all ' + $rows.Count + ' from ' + $ExpectedSource)
