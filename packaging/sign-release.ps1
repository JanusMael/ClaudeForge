#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Sign the Windows artifacts of a published ClaudeForge release with a Certum
    SimplySign cloud certificate, re-upload them in place, and dispatch the winget
    submission.

.DESCRIPTION
    Certum SimplySign can't be automated on hosted GitHub Actions (its cloud key
    is gated behind mobile-app MFA), so signing is a local per-release step. This
    script runs anywhere PowerShell 7 does; the SIGNING call adapts to the host:

      * Windows      -> signtool.exe (Windows SDK) against the SimplySign Desktop
                        virtual smart card. Authenticate SimplySign Desktop FIRST.
      * macOS/Linux  -> osslsigncode against the SimplySign PKCS#11 (p11-kit)
                        session (-Pkcs11Module required). See packaging/SIGNING.md.

    Per Windows zip asset:
      gh release download -> extract -> sign ClaudeForge.exe -> verify
      -> re-zip (same name/layout) -> gh release upload --clobber
    then: gh workflow run <winget workflow> -f version=<tag without 'v'>.

    winget stores a SHA256 that wingetcreate computes from the live asset, so the
    winget dispatch MUST run AFTER the signed re-upload — which is the order here.

.PARAMETER Tag
    Release tag to sign, e.g. v2026.3.701.

.PARAMETER Repo
    owner/repo. Defaults to the current directory's GitHub repo (via gh).

.PARAMETER CertSubject
    signtool /n subject-name selector (Windows). Default 'Brian Bennewitz'.

.PARAMETER CertThumbprint
    signtool /sha1 selector (Windows). Overrides -CertSubject when set — use it if
    several code-signing certs are present.

.PARAMETER TimestampUrl
    RFC3161 timestamp server. Default Certum's http://time.certum.pl.

.PARAMETER Assets
    Windows zip asset names to sign. Default the two release zips.

.PARAMETER WingetWorkflow
    winget workflow file to dispatch. Default winget-submit.yml.

.PARAMETER Pkcs11Module
    (Non-Windows) SimplySign p11-kit client library for osslsigncode.

.PARAMETER Pkcs11Cert
    (Non-Windows) PKCS#11 cert/key URI. Default 'pkcs11:model=SimplySign%20C'.

.PARAMETER SkipWinget
    Sign + re-upload only; do not dispatch the winget workflow.

.EXAMPLE
    pwsh packaging/sign-release.ps1 -Tag v2026.3.701
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Tag,

    [string] $Repo,
    [string] $CertSubject = 'Brian Bennewitz',
    [string] $CertThumbprint,
    [string] $TimestampUrl = 'http://time.certum.pl',
    [string[]] $Assets = @('ClaudeForge-win-x64.zip', 'ClaudeForge-win-arm64.zip'),
    [string] $WingetWorkflow = 'winget-submit.yml',
    [string] $Pkcs11Module,
    [string] $Pkcs11Cert = 'pkcs11:model=SimplySign%20C',
    [switch] $SkipWinget
)

$ErrorActionPreference = 'Stop'

$version = $Tag -replace '^v', ''
if (-not $version) { throw "Tag '$Tag' does not contain a version." }

# ── Preflight: gh present + authenticated ────────────────────────────────────
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) not found. Install it and run 'gh auth login'."
}
& gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "gh is not authenticated. Run 'gh auth login'." }

if (-not $Repo) {
    $Repo = (& gh repo view --json nameWithOwner --jq '.nameWithOwner')
    if ($LASTEXITCODE -ne 0 -or -not $Repo) {
        throw "Could not resolve the repo. Pass -Repo owner/name, or run from the repo directory."
    }
}
Write-Host "Repo: $Repo   Tag: $Tag   Version: $version" -ForegroundColor Cyan

# ── Locate the signing tool for this platform ────────────────────────────────
function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $roots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "${env:ProgramFiles}\Windows Kits\10\bin") |
        Where-Object { $_ -and (Test-Path $_) }
    foreach ($root in $roots) {
        $found = Get-ChildItem -Path $root -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '[\\/]x64[\\/]' } |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    return $null
}

$signTool = $null
if ($IsWindows) {
    $signTool = Find-SignTool
    if (-not $signTool) {
        throw "signtool.exe not found. Install the Windows 10/11 SDK (it ships signtool)."
    }
    Write-Host "signtool: $signTool"
    Write-Host "NOTE: authenticate SimplySign Desktop (mobile-app MFA) before running this." -ForegroundColor Yellow
}
else {
    if (-not (Get-Command osslsigncode -ErrorAction SilentlyContinue)) {
        throw "osslsigncode not found. Non-Windows signing needs it + the SimplySign PKCS#11 session (see packaging/SIGNING.md)."
    }
    if (-not $Pkcs11Module) {
        throw "Non-Windows signing needs -Pkcs11Module (SimplySign p11-kit client library). See packaging/SIGNING.md."
    }
}

function Invoke-Sign([string] $exePath) {
    if ($IsWindows) {
        $sel = if ($CertThumbprint) { @('/sha1', $CertThumbprint) } else { @('/n', $CertSubject) }
        & $signTool sign /fd sha256 /tr $TimestampUrl /td sha256 @sel $exePath
        if ($LASTEXITCODE -ne 0) {
            throw "signtool sign failed ($LASTEXITCODE). Is SimplySign Desktop authenticated and the cert present?"
        }
        & $signTool verify /pa /v $exePath
        if ($LASTEXITCODE -ne 0) { throw "signtool verify failed ($LASTEXITCODE)." }
    }
    else {
        $out = "$exePath.signed"
        & osslsigncode sign -pkcs11module $Pkcs11Module -pkcs11cert $Pkcs11Cert -key $Pkcs11Cert `
            -h sha256 -t $TimestampUrl -in $exePath -out $out
        if ($LASTEXITCODE -ne 0) { throw "osslsigncode sign failed ($LASTEXITCODE)." }
        Move-Item -Force -LiteralPath $out -Destination $exePath
        & osslsigncode verify -in $exePath
        if ($LASTEXITCODE -ne 0) { throw "osslsigncode verify failed ($LASTEXITCODE)." }
    }
}

# ── Work in a temp dir (cleaned up on exit) ──────────────────────────────────
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("claudeforge-sign-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
Write-Host "Work dir: $work"

try {
    foreach ($asset in $Assets) {
        Write-Host "`n=== $asset ===" -ForegroundColor Cyan
        $zipPath = Join-Path $work $asset
        $extractDir = Join-Path $work ([System.IO.Path]::GetFileNameWithoutExtension($asset))

        # 1. Download the release asset.
        Remove-Item -Force -ErrorAction SilentlyContinue -LiteralPath $zipPath
        & gh release download $Tag --repo $Repo --pattern $asset --dir $work
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $zipPath)) {
            throw "Failed to download '$asset' from $Repo release $Tag."
        }

        # 2. Extract.
        if (Test-Path $extractDir) { Remove-Item -Recurse -Force -LiteralPath $extractDir }
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force

        # 3. Find + sign the exe.
        $exe = Get-ChildItem -Path $extractDir -Recurse -Filter 'ClaudeForge.exe' | Select-Object -First 1
        if (-not $exe) { throw "ClaudeForge.exe not found inside '$asset'." }
        Invoke-Sign $exe.FullName
        Write-Host "Signed + verified: $($exe.Name)" -ForegroundColor Green

        # 4. Re-zip with the SAME name/layout (contents at the archive root, so the
        #    winget nested-portable RelativeFilePath 'ClaudeForge.exe' still matches).
        Remove-Item -Force -LiteralPath $zipPath
        Compress-Archive -Path (Join-Path $extractDir '*') -DestinationPath $zipPath -Force

        # 5. Replace the release asset in place (same URL).
        & gh release upload $Tag $zipPath --repo $Repo --clobber
        if ($LASTEXITCODE -ne 0) { throw "Failed to upload signed '$asset'." }
        Write-Host "Re-uploaded signed $asset" -ForegroundColor Green
    }
}
finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue -LiteralPath $work
}

# ── Kick off the winget submission (AFTER the signed re-upload) ───────────────
if ($SkipWinget) {
    Write-Host "`n-SkipWinget set — not dispatching the winget workflow." -ForegroundColor Yellow
}
else {
    Write-Host "`nDispatching $WingetWorkflow for $version ..." -ForegroundColor Cyan
    & gh workflow run $WingetWorkflow --repo $Repo -f version=$version
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to dispatch $WingetWorkflow. Trigger it manually: gh workflow run $WingetWorkflow -f version=$version"
    }
    Write-Host "winget submission dispatched. Track it: gh run list --workflow $WingetWorkflow" -ForegroundColor Green
}

Write-Host "`nDone. Signed + re-uploaded $($Assets.Count) asset(s) for $Tag." -ForegroundColor Green
