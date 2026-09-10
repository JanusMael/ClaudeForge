#Requires -Version 7
<#
.SYNOPSIS
    Submit one app's winget manifest to microsoft/winget-pkgs (local, interactive).

.DESCRIPTION
    Local counterpart to .github/workflows/winget-submit.yml. Builds the COMPLETE
    manifest from the templates in packaging/winget/*.yaml (the single source of
    truth, carrying the full catalog metadata), fills in the version + freshly
    computed SHA256 hashes, injects the GitHub release's notes + date, and opens
    the winget-pkgs PR via `wingetcreate submit`.

    This replaces the older `wingetcreate update ... --submit` command. `update`
    fetches the currently published manifest and only bumps the version + URLs, so
    it inherits whatever metadata is already published and drops the rich fields
    (Author, Description, Tags, ...). Submitting the template avoids that.

    Run this AFTER the release's Windows zips are signed and re-uploaded — winget
    pins a SHA256 computed from the live asset.

    Keep in sync with .github/workflows/winget-submit.yml.

.PARAMETER App
    Which app to submit — a Name from src/publish/PublishApps.ps1. Defaults to
    ClaudeForge. The descriptor supplies the winget package id, the release-tag
    prefix, and the assembly name the assets and signed .exe are named after;
    keeping all three in one table is what stops a manifest pointing at a URL no
    release publishes.

.PARAMETER Version
    Release version, no leading 'v' (e.g. 2026.3.725). Prompted if omitted.

.PARAMETER Token
    GitHub PAT (classic) with BOTH 'public_repo' and 'workflow' scopes. If omitted,
    uses $env:WINGET_TOKEN; if that is also empty, wingetcreate falls back to an
    interactive GitHub login (no token on the command line — the safer option).

.EXAMPLE
    .\Submit-Winget.ps1
    .\Submit-Winget.ps1 -Version 2026.3.725
    .\Submit-Winget.ps1 -App OpenCodeForge -Version 2026.4.100
#>
[CmdletBinding()]
param(
    [string]$App = 'ClaudeForge',
    [string]$Version,
    [string]$Token = $env:WINGET_TOKEN
)

$ErrorActionPreference = 'Stop'

# Decode child-process stdout as UTF-8. `gh` emits UTF-8; without this PowerShell
# decodes it using the console's OEM code page, so any non-ASCII character in the
# release body is mangled before it ever reaches the manifest — an em dash (U+2014,
# bytes E2 80 94) lands as "ΓÇö". Shipped that way in 2026.3.810; don't again.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# $appInfo, never $app — PowerShell variable names are case-insensitive, so a
# local named $app IS the $App parameter.
. (Join-Path $PSScriptRoot '..' 'src' 'publish' 'PublishApps.ps1')
$appInfo = Get-PublishApp -Name $App

# The repository keeps its original name while hosting both apps [decision 14]:
# published winget manifests cannot be retroactively repointed, so this is the
# release host for BOTH, and only the tag prefix distinguishes them.
$Repo        = 'JanusMael/ClaudeForge'
$PackageId   = $appInfo.WingetPackageId
$AssetStem   = $appInfo.AssemblyName
$TagPrefix   = $appInfo.TagPrefix
$TemplateDir = Join-Path $PSScriptRoot 'winget'

if (-not $Version) {
    $Version = Read-Host 'Release version to submit (no leading v, e.g. 2026.3.725)'
}
$Version = $Version -replace '^v', ''
if (-not $Version) { throw 'A version is required.' }

# Duplicate guard. There are three ways to submit — this script, a manual
# `gh workflow run winget-submit.yml`, and packaging/sign-release.ps1, which
# dispatches that workflow automatically unless you pass -SkipWinget. Running two
# of them for one release opens two PRs against the same manifest, which
# winget-pkgs explicitly asks contributors not to do (2026.3.810 did exactly this).
$existing = gh api "search/issues?q=repo:microsoft/winget-pkgs+author:JanusMael+type:pr+state:open+in:title+$PackageId" `
    --jq ".items[] | select(.title | contains(\"$Version\")) | \"#\(.number) \(.title)\"" 2>$null
if ($LASTEXITCODE -eq 0 -and $existing) {
    throw ("An open winget-pkgs PR already exists for $PackageId ${Version}:`n  $existing`n" +
           'Close it first, or let that submission finish. ' +
           'Note sign-release.ps1 already dispatches winget-submit.yml unless -SkipWinget is passed.')
}

# ⚠ The tag carries this app's prefix. ClaudeForge's is empty (it published this
# repository's releases before it hosted two and cannot change shape without
# blinding every installed copy); a later app's is not. Building the URL from a
# bare "v$Version" here would 404 for anything but the first app.
$tag      = "$TagPrefix" + "v$Version"
$base     = "https://github.com/$Repo/releases/download/$tag"
$x64Url   = "$base/$AssetStem-win-x64.zip"
$arm64Url = "$base/$AssetStem-win-arm64.zip"

Write-Host "Submitting $PackageId $Version" -ForegroundColor Cyan

# 1. Download the (signed) release assets and compute their SHA256. winget-pkgs
#    validation recomputes these from the same URLs, so they must match exactly.
$work  = Join-Path ([System.IO.Path]::GetTempPath()) "winget-$PackageId-$Version"
$dl    = Join-Path $work 'dl'
$stage = Join-Path $work 'manifests'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dl, $stage | Out-Null

Write-Host 'Downloading release assets...'
Invoke-WebRequest -Uri $x64Url   -OutFile (Join-Path $dl 'x64.zip')
Invoke-WebRequest -Uri $arm64Url -OutFile (Join-Path $dl 'arm64.zip')
# Signing gate — refuse to submit unsigned binaries. The release workflow publishes
# UNSIGNED zips; sign-release.ps1 signs them and re-uploads in place. Submitting
# before that pins the SHA256 of an unsigned asset and publishes it permanently.
# Verify the precondition rather than assuming the caller got the order right.
# Kept in sync with the same gate in .github/workflows/winget-submit.yml.
foreach ($arch in 'x64', 'arm64') {
    $ex = Join-Path $dl "extract-$arch"
    Expand-Archive -Path (Join-Path $dl "$arch.zip") -DestinationPath $ex -Force
    $exeName = "$AssetStem.exe"
    $exe = Get-ChildItem $ex -Filter $exeName -Recurse | Select-Object -First 1
    if (-not $exe) { throw "$arch zip does not contain $exeName — cannot verify signing." }
    $sig = Get-AuthenticodeSignature $exe.FullName
    Write-Host "  $arch Authenticode: $($sig.Status)"
    if ($sig.Status -ne 'Valid') {
        throw ("$arch $exeName is not validly signed (status: $($sig.Status)). " +
               'Sign and re-upload the release assets first — run packaging/sign-release.ps1.')
    }
}
Remove-Item (Join-Path $dl 'extract-x64'), (Join-Path $dl 'extract-arm64') -Recurse -Force

$sha64  = (Get-FileHash (Join-Path $dl 'x64.zip')   -Algorithm SHA256).Hash
$shaArm = (Get-FileHash (Join-Path $dl 'arm64.zip') -Algorithm SHA256).Hash
Write-Host "  x64   SHA256 $sha64"
Write-Host "  arm64 SHA256 $shaArm"

# 2. Stage the templates with version + hashes substituted in.
#
# ⛔ THIS APP'S MANIFESTS ONLY. `packaging/winget/` holds a set per app, and this
# used to glob '*.yaml' — correct while there was one app, and silently wrong the
# moment there were two: `wingetcreate submit` would have carried BOTH packages'
# manifests into one winget-pkgs PR, publishing a version bump for an app that
# had not released.
#
# Matched by exact name rather than "$PackageId*" so a future id that merely
# starts with another ("…ClaudeForge" vs a hypothetical "…ClaudeForgePro") cannot
# absorb its sibling's files.
$ownManifests = @(Get-ChildItem (Join-Path $TemplateDir '*.yaml') | Where-Object {
    $_.Name -eq "$PackageId.yaml" -or
    $_.Name -eq "$PackageId.installer.yaml" -or
    $_.Name -like "$PackageId.locale.*.yaml"
})

if ($ownManifests.Count -eq 0) {
    throw "No manifest templates for $PackageId in $TemplateDir. Expected " +
          "$PackageId.yaml, $PackageId.installer.yaml and at least one " +
          "$PackageId.locale.<tag>.yaml."
}

Write-Host ("  Staging {0} manifest(s) for {1}" -f $ownManifests.Count, $PackageId)
$ownManifests | ForEach-Object {
    $text = (Get-Content $_.FullName -Raw).
        Replace('<PACKAGE_VERSION>', $Version).
        Replace('<SHA256_X64>',   $sha64).
        Replace('<SHA256_ARM64>', $shaArm)
    Set-Content -Path (Join-Path $stage $_.Name) -Value $text -Encoding utf8
}
$localeFile    = Join-Path $stage "$PackageId.locale.en-US.yaml"
$installerFile = Join-Path $stage "$PackageId.installer.yaml"

# 3. Best-effort enrichment from the GitHub release: ReleaseDate (installer) and
#    ReleaseNotes (locale). Needs `gh`, authenticated. Failures degrade gracefully.
try {
    $raw = gh release view $tag --repo $Repo --json publishedAt,body 2>$null
    if ($LASTEXITCODE -eq 0 -and $raw) {
        $rel = $raw | ConvertFrom-Json
        if ($rel.publishedAt) {
            $date = ([datetimeoffset]$rel.publishedAt).ToString('yyyy-MM-dd')
            Add-Content -Path $installerFile -Value "ReleaseDate: $date" -Encoding utf8
            Write-Host "  ReleaseDate: $date"
        }
        if ($rel.body) {
            # Normalize newlines, trim blank edges, cap length (winget limit 10000),
            # then emit a YAML literal block with every line indented 2 spaces.
            $body = ($rel.body -replace "`r", '').Trim()
            if ($body.Length -gt 9000) { $body = $body.Substring(0, 9000).Trim() }
            $indented = ($body -split "`n" | ForEach-Object { '  ' + $_ }) -join "`n"
            Add-Content -Path $localeFile -Value "ReleaseNotes: |-`n$indented" -Encoding utf8
            Write-Host "  Injected ReleaseNotes ($($body.Length) chars)"
        }
    }
    else {
        Write-Warning "Could not read release $tag metadata; submitting without ReleaseNotes/ReleaseDate."
    }
}
catch {
    Write-Warning "Release enrichment failed ($($_.Exception.Message)); submitting without ReleaseNotes/ReleaseDate."
}

Write-Host "`n----- staged locale manifest -----" -ForegroundColor DarkGray
Get-Content $localeFile
Write-Host '----------------------------------' -ForegroundColor DarkGray

# 4. Fetch wingetcreate.
$wc = Join-Path $work 'wingetcreate.exe'
Write-Host "`nDownloading wingetcreate..."
Invoke-WebRequest -Uri https://aka.ms/wingetcreate/latest -OutFile $wc

# 5. Confirm, then submit. `submit` forks winget-pkgs, syncs the fork with
#    upstream, commits the manifests, and opens the PR.
Write-Host ''
$confirm = Read-Host "Submit PR for $PackageId $Version to microsoft/winget-pkgs? (y/N)"
if ($confirm -notmatch '^(y|yes)$') { Write-Host 'Aborted.' -ForegroundColor Yellow; return }

$wcArgs = @('submit', $stage, '--prtitle', "New version: $PackageId version $Version")
if ($Token) {
    # Note: passing --token puts it on the command line (visible to process listing
    # / shell history). Leave -Token / $env:WINGET_TOKEN unset to use wingetcreate's
    # interactive GitHub login instead.
    $wcArgs += @('--token', $Token)
}
& $wc @wcArgs

Write-Host "`nDone. Review the PR at https://github.com/microsoft/winget-pkgs/pulls" -ForegroundColor Green
