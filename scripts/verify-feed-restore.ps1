#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Prove the app restores the eleven shared packages FROM THE PUBLISHED FEED.

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

# The eleven. Listed rather than discovered, deliberately: discovery that finds ten and reports
# ten green is the failure this file is guarding against.
$expectedIds = @(
    'bennewitz.ninja.agentforge.abstractions'
    'bennewitz.ninja.agentforge.artifacts'
    'bennewitz.ninja.agentforge.avalonia.shell'
    'bennewitz.ninja.agentforge.core'
    'bennewitz.ninja.agentforge.sdk'
    'bennewitz.ninja.jsonc'
    'bennewitz.ninja.layerededitors.abstractions'
    'bennewitz.ninja.layerededitors.avalonia'
    'bennewitz.ninja.layerededitors.avalonia.diagnostics'
    'bennewitz.ninja.layerededitors.avalonia.services'
    'bennewitz.ninja.layerededitors.viewmodels'
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
