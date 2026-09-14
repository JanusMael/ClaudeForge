#!/usr/bin/env pwsh
# Resolve-ReleaseVersion.ps1 — turn a release TAG into the ONE version input a build needs.
#
# ══ WHY THIS EXISTS ══
#
# The version every artifact carries is a CalVer stamp, YEAR.QUARTER.MMdd, computed by the
# AutoVersioning package from a single captured instant. A release has to pin that instant to
# the TAG rather than to whenever CI happened to run, or re-running a release days later
# publishes different numbers from the same source.
#
# ⛔ THERE IS EXACTLY ONE KNOB THAT WORKS, AND THE TWO OBVIOUS ONES ARE WORSE THAN USELESS.
# Measured against AutoVersioning 2026.3.914, building one packable project three ways:
#
#   -p:PublicVersion=2026.3.901        package 2026.3.914   assembly 2026.3.914.1346   NO-OP
#   -p:AutoPackageVersion=2026.3.901   package 2026.3.901   assembly 2026.3.914.1347   SKEW
#   -p:BuildTimestamp=20260901120000   package 2026.3.901   assembly 2026.3.901.1200   ✅
#
# The reason is in that package's own props: BuildTimestamp is a CompilerVisibleProperty, so the
# generator that stamps the assembly sees it. AutoVersion and AutoPackageVersion are not in that
# list — they are MSBuild-side only, so pinning them moves the PACKAGE version and leaves the
# assembly inside it where it was.
#
# ⚠ PublicVersion IS NOT INERT, and calling it dead was wrong. It is compiler-visible, and the
# generator writes it into the assembly as [AssemblyMetadata("PublicVersion", …)] — measured, the
# attribute is there. What it does not do is set the VERSION, which is exactly the trap: it is
# that package's documented CI-version input, so it reads like the knob while the numbers come
# from somewhere else entirely.
#
# ⭐ THAT METADATA IS WORTH KEEPING FOR ONE REASON: A PRERELEASE SUFFIX SURVIVES NOWHERE ELSE.
# AssemblyVersion and FileVersion are numeric, so v2026.3.914-rc.1 and v2026.3.914 both stamp
# 2026.3.914.0, and InformationalVersion is a fixed string ("Built with ♥"). Without this
# attribute an rc binary and its final release are indistinguishable from the file alone.
#
# ══ WHAT IT EMITS ══
#
#   BuildTimestamp   yyyyMMdd000000 — midnight LOCAL on the tag's own date. Local because the
#                    generator parses it with DateTimeStyles.AssumeLocal. Midnight because a
#                    release must be reproducible: the same tag built twice produces the same
#                    version, which matters because a package feed refuses a re-push.
#   ReleaseVersion   the tag with its prefix stripped, prerelease suffix included.
#   PublicVersion    the same string as ReleaseVersion, under the name the generator reads it by.
#
# ⛔ PublicVersion IS EMITTED HERE AND NOWHERE ELSE, and that is the actual fix. The bug was never
# the property — it was a workflow setting it independently while believing it carried the
# version. One derivation emitting both means they cannot disagree about which tag they came from.
# ReleaseWorkflowTests.NoWorkflowSetsPublicVersion keeps the second setter from reappearing.
#
# All three are appended to $GITHUB_ENV when running under Actions, where they reach MSBuild
# through its environment-variable-to-property mapping — and the props file's own Condition
# leaves an already-supplied BuildTimestamp alone.
#
# ⚠ AutoVersion's fourth part is HHmm, so a tag release stamps x.y.z.0 by construction. That is
# the reproducibility, not a rounding error.
#
# USAGE
#   ./scripts/Resolve-ReleaseVersion.ps1                                  # reads GITHUB_REF_NAME
#   ./scripts/Resolve-ReleaseVersion.ps1 -Tag v2026.3.914
#   ./scripts/Resolve-ReleaseVersion.ps1 -Tag opencodeforge-v2026.3.914 -TagPrefix opencodeforge-v

[CmdletBinding()]
param(
    # Defaults to the tag Actions is running for. Named Tag rather than Ref because a ref is
    # refs/tags/x and this wants the bare name.
    [string] $Tag,

    # The prefix to strip. ClaudeForge publishes bare 'v' tags; OpenCodeForge is prefixed,
    # because releases are repository-level and an app finds its own by prefix.
    [string] $TagPrefix = 'v'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# YEAR.QUARTER.MMdd, with MMdd's leading zero dropped exactly as the generator drops it —
# 09/14 is 914, not 0914 — plus an optional SemVer prerelease suffix.
$CalVerPattern = '^(?<year>\d{4})\.(?<quarter>[1-4])\.(?<mmdd>\d{3,4})(?<suffix>-[0-9A-Za-z][0-9A-Za-z.\-]*)?$'

function Get-QuarterOfMonth {
    param([int] $Month)

    # The props file spells this as integer (Month + 2) / 3 and notes that MSBuild's Divide
    # returns a double. Ceiling over a real division is the same function, stated in a language
    # that has one.
    return [int] [math]::Ceiling($Month / 3.0)
}

function Resolve-Tag {
    param([string] $TagName, [string] $Prefix)

    if ([string]::IsNullOrWhiteSpace($TagName)) {
        throw 'No tag to resolve. Pass -Tag, or run where GITHUB_REF_NAME is set.'
    }

    if (-not $TagName.StartsWith($Prefix, [System.StringComparison]::Ordinal)) {
        throw ("Tag '" + $TagName + "' does not start with the expected prefix '" + $Prefix +
            "'. A workflow strips its own app's prefix; a tag that reached the wrong workflow " +
            'would otherwise be published under the wrong app.')
    }

    $bare = $TagName.Substring($Prefix.Length)
    $match = [regex]::Match($bare, $CalVerPattern)

    if (-not $match.Success) {
        throw ("Tag '" + $TagName + "' does not carry a CalVer version. Expected " + $Prefix +
            "YEAR.QUARTER.MMdd, e.g. " + $Prefix + '2026.3.914, optionally with a prerelease ' +
            "suffix. Got '" + $bare + "'.")
    }

    $year = [int] $match.Groups['year'].Value
    $quarter = [int] $match.Groups['quarter'].Value
    $mmdd = $match.Groups['mmdd'].Value.PadLeft(4, '0')

    $month = [int] $mmdd.Substring(0, 2)
    $day = [int] $mmdd.Substring(2, 2)

    # ⛔ A malformed tag must fail HERE. Every downstream consumer publishes something immutable.
    try {
        $null = [datetime]::new($year, $month, $day)
    }
    catch {
        throw ("Tag '" + $TagName + "' encodes " + $year + '-' + $mmdd +
            ', which is not a real date.')
    }

    $expectedQuarter = Get-QuarterOfMonth -Month $month
    if ($quarter -ne $expectedQuarter) {
        throw ("Tag '" + $TagName + "' says quarter " + $quarter + ' but month ' + $month +
            ' is in quarter ' + $expectedQuarter + '. The middle part is not free-form: the ' +
            'build derives it from the date, so a tag that disagrees would publish a version ' +
            'nobody can reproduce from this tag.')
    }

    return [pscustomobject] @{
        ReleaseVersion = $bare
        BuildTimestamp = ('{0:d4}{1:d2}{2:d2}000000' -f $year, $month, $day)
    }
}

function Main {
    $tagName = if ($Tag) { $Tag } else { $env:GITHUB_REF_NAME }
    $resolved = Resolve-Tag -TagName $tagName -Prefix $TagPrefix

    Write-Host ('ReleaseVersion = ' + $resolved.ReleaseVersion + '   (the version, for humans)')
    Write-Host ('BuildTimestamp = ' + $resolved.BuildTimestamp + '   (what actually stamps it)')
    Write-Host ('PublicVersion  = ' + $resolved.ReleaseVersion + '   (assembly metadata only)')

    if ($env:GITHUB_ENV) {
        Add-Content -Path $env:GITHUB_ENV -Value ('ReleaseVersion=' + $resolved.ReleaseVersion)
        Add-Content -Path $env:GITHUB_ENV -Value ('BuildTimestamp=' + $resolved.BuildTimestamp)
        Add-Content -Path $env:GITHUB_ENV -Value ('PublicVersion=' + $resolved.ReleaseVersion)
    }
}

# Guarded on an env var rather than $MyInvocation.InvocationName: the VS Code extension
# dot-sources on F5, where InvocationName IS '.', so the conventional guard never runs Main.
if (-not $env:RESOLVE_RELEASE_VERSION_NOEXEC) {
    Main
}
