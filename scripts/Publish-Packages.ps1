#!/usr/bin/env pwsh
# Publish-Packages.ps1 — pack the six shared libraries and push them, once, at one version.
#
# Plan 00001 work item 6.
#
# ══ WHAT MAKES THIS DIFFERENT FROM EVERY OTHER SCRIPT HERE ══
#
# ⛔ A PUBLISHED PACKAGE VERSION CANNOT BE REPLACED OR RE-PUSHED. Everything below is shaped by
# that one fact. A set that fails on package 7 leaves six published and immutable, and the only
# recovery is bumping all six — which, under a day-resolution CalVer, means waiting for
# tomorrow. So this script's job is to find every reason to refuse BEFORE the first push, and to
# refuse loudly rather than push a partial set.
#
# Three gates, in order, all before anything is uploaded:
#
#   1. Pack produces a set at exactly one version, and that version is the one asked for.
#   2. Every package's contained assembly carries that same stamp. A package whose DLL says a
#      different day is publishable, silently wrong, and permanent.
#   3. The feed does not already have any of them at that version.
#
# ⛔ GATE 3 CANNOT TELL "NOT PUBLISHED" FROM "NOT AUTHORISED" BY ITSELF, AND THE FIRST VERSION OF
# THIS SCRIPT GOT IT WRONG. Measured against this feed:
#
#   bad token,  /<id>/index.json    404      <- identical to an unpublished id
#   no auth,    /<id>/index.json    401
#   bad token,  /index.json         200      <- the service index proves NOTHING
#
# So the obvious reading — "404 means absent" — hands a clean preflight to anyone whose token is
# missing, expired or garbled. Canaried: the first version passed gate 3 with the literal token
# "definitely-not-a-valid-token".
#
# The credentials are therefore proved FIRST, against api.github.com/rate_limit, which answers
# 401 for a bad token and 200 for any live one — PAT or Actions GITHUB_TOKEN alike. Only then is
# a 404 from the feed read as "absent".
#
# ⚠ ONE RESIDUAL GAP, STATED RATHER THAN PAPERED OVER: a token with write:packages but not
# read:packages is live, so it passes the probe, and still 404s on every read. The push order
# below is what covers that — packages go one at a time in a stable order and the run stops on
# the first failure, so a duplicate is caught at package 1 with the other ten untouched.
#
# USAGE
#   pwsh -NoProfile -File scripts/Publish-Packages.ps1 -PackageVersion 2026.3.914 -PreflightOnly
#   pwsh -NoProfile -File scripts/Publish-Packages.ps1 -PackageVersion 2026.3.914
#
# The version normally comes from the tag, via scripts/Resolve-ReleaseVersion.ps1 — see the
# release-packages workflow. BuildTimestamp must be set to the matching instant (the resolver
# exports it) or gate 2 refuses the run, which is the point: it is what ties the assemblies
# inside the packages to the tag on the outside.

[CmdletBinding()]
param(
    # The version to publish. Named PackageVersion rather than Version: param() overwrites the
    # caller's variables with its own defaults when this file is dot-sourced, and $Version is a
    # name a host is likely to hold.
    [Parameter(Mandatory)]
    [string] $PackageVersion,

    # The nuget.config source key to publish to.
    [string] $Source = 'github',

    # Base address for the version query. Must be the same feed $Source resolves to; kept as a
    # parameter so the preflight can be pointed at a test feed without editing nuget.config.
    [string] $FeedIndexBase = 'https://nuget.pkg.github.com/JanusMael',

    # Defaults to $env:GITHUB_TOKEN, which is what Actions supplies with packages: write.
    [string] $ApiKey,

    # The account the feed authenticates the read query as. GitHub Packages accepts any username
    # alongside a valid token, but it must not be empty.
    [string] $FeedUser,

    [string] $OutputDirectory,

    # Run the three gates and stop. Nothing is uploaded.
    [switch] $PreflightOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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

<#
.SYNOPSIS
    The id, version, and contained assembly version of one .nupkg.
#>
function Read-PackageIdentity {
    param([string] $NupkgPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $zip = [System.IO.Compression.ZipFile]::OpenRead($NupkgPath)
    try {
        $nuspecEntry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if (-not $nuspecEntry) {
            throw ("'" + [System.IO.Path]::GetFileName($NupkgPath) + "' contains no .nuspec.")
        }

        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try { $nuspec = [xml] $reader.ReadToEnd() } finally { $reader.Dispose() }

        $assemblyVersion = $null
        $dllEntry = $zip.Entries | Where-Object { $_.FullName -like 'lib/*.dll' } | Select-Object -First 1
        if ($dllEntry) {
            $temp = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString('n') + '.dll')
            try {
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($dllEntry, $temp, $true)
                $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($temp).Version.ToString()
            }
            finally {
                Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue
            }
        }
    }
    finally {
        $zip.Dispose()
    }

    return [pscustomobject] @{
        Path = $NupkgPath
        Id = $nuspec.package.metadata.id
        Version = $nuspec.package.metadata.version
        AssemblyVersion = $assemblyVersion
    }
}

<#
.SYNOPSIS
    The numeric part of a version, without any prerelease suffix.
#>
function Get-NumericVersion {
    param([string] $Value)

    $dash = $Value.IndexOf('-')
    if ($dash -ge 0) {
        return $Value.Substring(0, $dash)
    }

    return $Value
}

<#
.SYNOPSIS
    The first three parts of a dotted version.
#>
function Get-ThreePartStamp {
    param([string] $Value)

    $parts = (Get-NumericVersion -Value $Value).Split('.')
    if ($parts.Length -lt 3) {
        return $Value
    }

    return ($parts[0] + '.' + $parts[1] + '.' + $parts[2])
}

<#
.SYNOPSIS
    Throws unless the token is live.
.DESCRIPTION
    ⛔ This is what makes a 404 from the package feed mean anything. That feed answers 404 for an
    unpublished id AND for a bad token, so without this probe an expired token produces a clean
    preflight over a feed nobody could read. rate_limit is used because it is the one endpoint
    that answers for every credential type this script sees — a developer's PAT and Actions'
    GITHUB_TOKEN — and distinguishes 401 from 200 without needing any particular scope.
#>
function Assert-CredentialsUsable {
    param([string] $Token)

    try {
        $null = Invoke-RestMethod -Uri 'https://api.github.com/rate_limit' -Method Get -ErrorAction Stop `
            -Headers @{ Authorization = 'Bearer ' + $Token; 'User-Agent' = 'Publish-Packages.ps1' }
    }
    catch {
        $status = $null
        if ($_.Exception.PSObject.Properties.Name -contains 'Response' -and $_.Exception.Response) {
            $status = [int] $_.Exception.Response.StatusCode
        }

        throw ('The token is not usable (HTTP ' + [string] $status + ' from api.github.com). ' +
            'Stopping here rather than reading the package feed, because that feed answers 404 ' +
            'for a bad token exactly as it does for an unpublished id — so the version check ' +
            'would pass while checking nothing. Actions supplies GITHUB_TOKEN with ' +
            'packages: write; a local run needs a PAT carrying read:packages and write:packages.')
    }
}

<#
.SYNOPSIS
    $true when the feed already holds $Id at $Version.
.DESCRIPTION
    404 means absent — but ONLY because Assert-CredentialsUsable has already run; this feed
    returns 404 for a bad token too. Every other failure throws, because a preflight that cannot
    ask is not a preflight that passed.
#>
function Test-VersionPublished {
    param([string] $Id, [string] $Version, [string] $IndexBase, [hashtable] $Headers)

    $url = $IndexBase.TrimEnd('/') + '/' + $Id.ToLowerInvariant() + '/index.json'

    try {
        $response = Invoke-RestMethod -Uri $url -Headers $Headers -Method Get -ErrorAction Stop
    }
    catch {
        $status = $null
        if ($_.Exception.PSObject.Properties.Name -contains 'Response' -and $_.Exception.Response) {
            $status = [int] $_.Exception.Response.StatusCode
        }

        if ($status -eq 404) {
            # The id has never been published. Nothing to collide with.
            return $false
        }

        throw ('Could not ask the feed whether ' + $Id + ' ' + $Version + ' exists (' +
            'HTTP ' + [string] $status + ' from ' + $url + '). A preflight that cannot ask is ' +
            'not a preflight that passed, and pushing past it risks a partial, permanent set. ' +
            'Check the token has read access to the feed.')
    }

    # The flat-container index is { "versions": [ ... ] }. Compare case-insensitively: NuGet
    # versions are not case-sensitive and a prerelease label may differ in case.
    $versions = @()
    if ($response -and $response.PSObject.Properties.Name -contains 'versions') {
        $versions = @($response.versions)
    }

    foreach ($existing in $versions) {
        if ([string]::Equals([string] $existing, $Version, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Main {
    $repoRoot = Get-RepoRoot
    $solution = Join-Path $repoRoot 'ClaudeForge.slnx'

    $token = if ($ApiKey) { $ApiKey } else { $env:GITHUB_TOKEN }
    $user = if ($FeedUser) { $FeedUser } else { $env:GITHUB_ACTOR }

    if (-not $PreflightOnly -and [string]::IsNullOrWhiteSpace($token)) {
        throw 'No API key. Pass -ApiKey, or set GITHUB_TOKEN (Actions supplies it with packages: write).'
    }

    $output = if ($OutputDirectory) {
        $OutputDirectory
    }
    else {
        Join-Path $repoRoot (Join-Path 'artifacts/release-packages' $PackageVersion)
    }

    if (Test-Path $output) {
        Remove-Item -Recurse -Force $output
    }
    New-Item -ItemType Directory -Path $output -Force | Out-Null

    # ⚠ NOT -p:UseSharedPackages=true. The six reference each other by project in both modes;
    # asking them to consume packages of themselves that do not exist yet is the one way to make
    # pack fail for a reason that has nothing to do with packing.
    Invoke-Dotnet -What 'pack' -DotnetArgs @(
        'pack', $solution,
        '-c', 'Release',
        '-o', $output,
        "-p:PackageVersion=$PackageVersion",
        '--nologo'
    )

    $packages = @(Get-ChildItem -Path $output -Filter '*.nupkg' -File | Sort-Object Name)
    if ($packages.Count -eq 0) {
        throw ('pack produced no packages. Check that the shared projects still state ' +
            '<IsPackable>true</IsPackable>.')
    }

    Write-Host ''
    Write-Host ('── Gate 1: ' + $packages.Count + ' package(s), one version ──') -ForegroundColor Cyan

    $identities = @($packages | ForEach-Object { Read-PackageIdentity -NupkgPath $_.FullName })

    $wrongVersion = @($identities | Where-Object { $_.Version -ne $PackageVersion })
    if ($wrongVersion.Count -gt 0) {
        $detail = ($wrongVersion | ForEach-Object { $_.Id + ' = ' + $_.Version }) -join ', '
        throw ('These packages did not pack at ' + $PackageVersion + ': ' + $detail +
            '. A mixed set on the feed lets a consumer resolve two copies of a shared library.')
    }

    Write-Host ''
    Write-Host '── Gate 2: each package carries the assembly it was stamped from ──' -ForegroundColor Cyan

    $expectedStamp = Get-ThreePartStamp -Value $PackageVersion
    $skewed = @($identities | Where-Object {
            $_.AssemblyVersion -and (Get-ThreePartStamp -Value $_.AssemblyVersion) -ne $expectedStamp
        })

    if ($skewed.Count -gt 0) {
        $detail = ($skewed | ForEach-Object { $_.Id + ' package ' + $_.Version + ' vs assembly ' + $_.AssemblyVersion }) -join '; '
        throw ('These packages contain an assembly stamped differently from the package version (' +
            $detail + '). BuildTimestamp is what ties the two together — the release workflow ' +
            'exports it from the tag, and a local run has to set it. Publishing this would put a ' +
            'permanent, silent lie on the feed.')
    }

    foreach ($identity in $identities) {
        Write-Host ('   ' + $identity.Id + ' ' + $identity.Version + '  (assembly ' + $identity.AssemblyVersion + ')')
    }

    Write-Host ''
    Write-Host '── Gate 3: the feed does not already hold this version ──' -ForegroundColor Cyan

    if ([string]::IsNullOrWhiteSpace($token)) {
        Write-Host '   SKIPPED — no token, and -PreflightOnly was requested.' -ForegroundColor Yellow
    }
    else {
        Assert-CredentialsUsable -Token $token

        if ([string]::IsNullOrWhiteSpace($user)) {
            $user = 'x-access-token'
        }

        $basic = [System.Convert]::ToBase64String(
            [System.Text.Encoding]::ASCII.GetBytes($user + ':' + $token))
        $headers = @{ Authorization = 'Basic ' + $basic }

        $already = @()
        foreach ($identity in $identities) {
            if (Test-VersionPublished -Id $identity.Id -Version $PackageVersion -IndexBase $FeedIndexBase -Headers $headers) {
                $already += $identity.Id
            }
        }

        if ($already.Count -gt 0) {
            throw ('These packages are ALREADY published at ' + $PackageVersion + ': ' +
                ($already -join ', ') + '. A published version cannot be replaced, so nothing ' +
                'was pushed. The version is derived from the tag date, so a second release on ' +
                'the same day collides by construction: tag tomorrow, or publish a prerelease ' +
                'version — noting that a prerelease sorts BELOW the plain version and will not ' +
                'supersede it for consumers.')
        }

        Write-Host ('   none of the ' + $identities.Count + ' ids holds ' + $PackageVersion)
    }

    if ($PreflightOnly) {
        Write-Host ''
        Write-Host ('Preflight PASSED for ' + $PackageVersion + '. Nothing was pushed.') -ForegroundColor Green
        return
    }

    Write-Host ''
    Write-Host '── Push ──' -ForegroundColor Cyan

    # ⭐ ONE AT A TIME, IN A STABLE ORDER, STOPPING ON THE FIRST FAILURE — this is the safety net
    # under gate 3, not a slower way of doing the same thing. A previous run that died partway
    # leaves a PREFIX of this same ordering published, so the very first push is the one that
    # collides, and the other ten are never attempted.
    #
    # --skip-duplicate is deliberately NOT passed. Gate 3 has already established that none of
    # these exists; if one appears anyway — a concurrent release, or a token that can write but
    # not read — it must fail rather than be quietly tolerated.
    foreach ($identity in $identities) {
        Invoke-Dotnet -What ('push ' + $identity.Id) -DotnetArgs @(
            'nuget', 'push', $identity.Path,
            '--source', $Source,
            '--api-key', $token
        )
    }

    Write-Host ''
    Write-Host ('Published ' + $identities.Count + ' package(s) at ' + $PackageVersion) -ForegroundColor Green
}

# Guarded on an env var rather than $MyInvocation.InvocationName: the VS Code extension
# dot-sources on F5, where InvocationName IS '.', so the conventional guard never runs Main.
if (-not $env:PUBLISH_PACKAGES_NOEXEC) {
    Main
}
