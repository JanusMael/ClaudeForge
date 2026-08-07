#Requires -Version 7
<#
.SYNOPSIS
    Submit the ClaudeForge winget manifest to microsoft/winget-pkgs (local, interactive).

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

.PARAMETER Version
    Release version, no leading 'v' (e.g. 2026.3.725). Prompted if omitted.

.PARAMETER Token
    GitHub PAT (classic) with BOTH 'public_repo' and 'workflow' scopes. If omitted,
    uses $env:WINGET_TOKEN; if that is also empty, wingetcreate falls back to an
    interactive GitHub login (no token on the command line — the safer option).

.EXAMPLE
    .\Submit-Winget.ps1
    .\Submit-Winget.ps1 -Version 2026.3.725
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Token = $env:WINGET_TOKEN
)

$ErrorActionPreference = 'Stop'

$Repo        = 'JanusMael/ClaudeForge'
$PackageId   = 'Bennewitz.Ninja.ClaudeForge'
$TemplateDir = Join-Path $PSScriptRoot 'winget'

if (-not $Version) {
    $Version = Read-Host 'Release version to submit (no leading v, e.g. 2026.3.725)'
}
$Version = $Version -replace '^v', ''
if (-not $Version) { throw 'A version is required.' }

$tag      = "v$Version"
$base     = "https://github.com/$Repo/releases/download/$tag"
$x64Url   = "$base/ClaudeForge-win-x64.zip"
$arm64Url = "$base/ClaudeForge-win-arm64.zip"

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
$sha64  = (Get-FileHash (Join-Path $dl 'x64.zip')   -Algorithm SHA256).Hash
$shaArm = (Get-FileHash (Join-Path $dl 'arm64.zip') -Algorithm SHA256).Hash
Write-Host "  x64   SHA256 $sha64"
Write-Host "  arm64 SHA256 $shaArm"

# 2. Stage the templates with version + hashes substituted in.
Get-ChildItem (Join-Path $TemplateDir '*.yaml') | ForEach-Object {
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
