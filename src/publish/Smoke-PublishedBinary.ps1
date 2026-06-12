<#
.SYNOPSIS
    Pre-release smoke: publish a self-contained binary for a RID, launch it
    briefly, and inspect for crash / asset-bundling / lifetime regressions.

.DESCRIPTION
    Catches the kind of failures that don't show up in `dotnet test` because
    they're trim-link-stage or runtime-only:

      - Native interop lifetime bugs (e.g. the SKSvg use-after-free that
        crashed the app at startup with 0xC0000005 in commit 7ce5de6).
      - Asset bundling problems (`avares://` resources missing from the
        single-file binary).
      - `<TrimmerRootAssembly>` gaps (rooted assemblies whose internal
        reflection still walks into trimmed methods).
      - First-frame Avalonia dispatcher exceptions caught by the
        AppDomain-unhandled handler.

    The smoke runs:
      1. `dotnet publish -c Release -r <rid>` (re-uses Publish-Rid.ps1).
      2. Starts the published binary with TimeoutSeconds (default 10s).
      3. After timeout, kills the process and asserts:
         - The process did NOT exit on its own before the timeout
           (early exit = boot-time crash).
         - A log file was created in the deploy's `logs/` directory.
         - The log contains the expected "Starting ClaudeForge" line.
         - The log contains NO `[FTL]` / `[ERR]` lines from the unhandled
           exception bridge or fatal-shutdown path.

    Returns 0 on success, non-zero on any failure.  Designed for ad-hoc
    pre-release verification on the developer's machine; the structure
    is CI-friendly (Windows / macOS / Linux runners with display servers
    can wrap this in a workflow).

    On Linux without a display server (headless CI), Avalonia will fail
    to initialise its X11/Wayland backend and the binary will exit with
    a non-zero code.  Either run with Xvfb or skip Linux smoke until
    a CI display layer is provisioned.

.PARAMETER Rid
    .NET runtime identifier to smoke.  Defaults to host RID auto-detect
    (win-x64 on Windows, osx-arm64 on Apple Silicon, linux-x64 on x64 Linux).

.PARAMETER TimeoutSeconds
    How long to let the binary run before killing it.  Default 10s.
    Increase for slow CI runners; decrease (~5s) for fast local iteration.

.PARAMETER SkipPublish
    When set, skips the `dotnet publish` step and assumes a binary already
    exists at the expected path.  Useful when iterating on the smoke logic
    itself.

.EXAMPLE
    pwsh src/publish/Smoke-PublishedBinary.ps1
    # Auto-detects host RID, publishes, smokes, exits 0 on success.

.EXAMPLE
    pwsh src/publish/Smoke-PublishedBinary.ps1 -Rid win-x64 -TimeoutSeconds 20
    # Explicit RID, longer timeout for cold caches.

.EXAMPLE
    pwsh src/publish/Smoke-PublishedBinary.ps1 -SkipPublish
    # Smoke an existing publish output without re-publishing.

.NOTES
    Created 2026-05 as part of the v1 release-readiness work (action C4).
    See CHANGELOG.md and AGENTS.md item I-trim for context.
#>

[CmdletBinding()]
param(
    [string] $Rid             = $null,
    [int]    $TimeoutSeconds  = 10,
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

# ─── 1. Resolve RID ───────────────────────────────────────────────────────
if (-not $Rid) {
    $Rid = if ($IsWindows) {
        if ([Environment]::Is64BitOperatingSystem -and -not $env:PROCESSOR_ARCHITECTURE.StartsWith('ARM')) {
            'win-x64'
        } else {
            'win-arm64'
        }
    } elseif ($IsMacOS) {
        if ((uname -m) -eq 'arm64') { 'osx-arm64' } else { 'osx-x64' }
    } elseif ($IsLinux) {
        if ((uname -m) -eq 'aarch64') { 'linux-arm64' } else { 'linux-x64' }
    } else {
        throw 'Unable to auto-detect host RID; pass -Rid explicitly.'
    }
    Write-Host "[smoke] auto-detected RID: $Rid"
}

# ─── 2. Publish ───────────────────────────────────────────────────────────
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$pubDir   = Join-Path $repoRoot "src/ClaudeForge/bin/Release/net10.0/$Rid/publish"

if (-not $SkipPublish) {
    Write-Host "[smoke] publishing $Rid..."
    # -p:RunResxKeyGuard=false skips the dev/CI unused-resx-key guard during publish
    # (see Publish-Rid.ps1 / Directory.Build.targets) — the inline guard task can flake
    # under concurrent-build / temp-dir contention and isn't needed for the binary.
    & dotnet publish (Join-Path $repoRoot 'src/ClaudeForge/ClaudeForge.csproj') `
        -c Release -r $Rid --nologo -v minimal `
        -p:RunResxKeyGuard=false
    if ($LASTEXITCODE -ne 0) {
        Write-Error "[smoke] publish failed (exit $LASTEXITCODE) — boot smoke aborted."
        exit 1
    }
} else {
    Write-Host "[smoke] -SkipPublish set; using existing binary at $pubDir"
}

# ─── 3. Locate the binary ────────────────────────────────────────────────
$exeName = if ($Rid.StartsWith('win-')) { 'ClaudeForge.exe' } else { 'ClaudeForge' }
$exePath = Join-Path $pubDir $exeName
if (-not (Test-Path $exePath)) {
    Write-Error "[smoke] expected binary not found at $exePath"
    exit 1
}
Write-Host "[smoke] binary: $exePath"

# ─── 4. Boot, sleep, kill ────────────────────────────────────────────────
# Windows: Avalonia opens a real window; we want the process running, not
#   a hung modal.
# macOS: same; .app bundling is a separate concern (out of scope for v1).
# Linux: requires X11 / Wayland display.  If $env:DISPLAY / $env:WAYLAND_DISPLAY
#   is unset, Avalonia X11 platform init fails fast — that's a legitimate
#   smoke failure on a headless CI runner.

$logsDir = Join-Path $pubDir 'logs'
if (Test-Path $logsDir) {
    # Clean prior smoke runs' logs so we only inspect this run's output.
    Get-ChildItem $logsDir -Filter 'app-*.txt' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Host "[smoke] launching for $TimeoutSeconds seconds..."
$proc = Start-Process -FilePath $exePath -PassThru -WorkingDirectory $pubDir
$earlyExit = $proc.WaitForExit([int]($TimeoutSeconds * 1000))

if ($earlyExit) {
    Write-Error "[smoke] process exited on its own before timeout (exit code $($proc.ExitCode)) — boot crash"
    if (Test-Path $logsDir) {
        $latest = Get-ChildItem $logsDir -Filter 'app-*.txt' -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($latest) {
            Write-Host "[smoke] last log lines:`n"
            Get-Content $latest.FullName -Tail 50 | ForEach-Object { Write-Host "  $_" }
        }
    }
    exit 2
}

Write-Host "[smoke] still running at timeout — killing"
try {
    $proc.Kill()
    $proc.WaitForExit(5000) | Out-Null
} catch {
    Write-Warning "[smoke] kill failed: $_"
}

# ─── 5. Inspect log ──────────────────────────────────────────────────────
if (-not (Test-Path $logsDir)) {
    Write-Error "[smoke] no logs/ directory created — Serilog never wrote anything; boot likely crashed before logging configured"
    exit 3
}

$latest = Get-ChildItem $logsDir -Filter 'app-*.txt' -ErrorAction SilentlyContinue |
          Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $latest) {
    Write-Error "[smoke] no app-*.txt log file found in $logsDir"
    exit 3
}
Write-Host "[smoke] log: $($latest.FullName)"
$logContent = Get-Content $latest.FullName

# Required: the startup line must appear.
if (-not ($logContent -match 'Starting ClaudeForge')) {
    Write-Error "[smoke] log missing 'Starting ClaudeForge' line — boot didn't reach Program.Main's startup log"
    exit 4
}

# Failure: any [FTL] / Fatal / unhandled-exception lines.
$fatalLines = $logContent | Where-Object {
    $_ -match '\[FTL\]' -or
    $_ -match 'AppDomain\.UnhandledException' -or
    $_ -match 'Fatal error during Avalonia bootstrap' -or
    $_ -match 'Unhandled exception'
}
if ($fatalLines) {
    Write-Error "[smoke] fatal log lines detected:"
    $fatalLines | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 5
}

# Soft check: surface error/warning lines for the operator's eyes,
# but don't fail on them — Avalonia's binding/control warnings are
# common and not always actionable.
$noisy = $logContent | Where-Object { $_ -match '\[ERR\]' }
if ($noisy) {
    Write-Host "[smoke] (info) $($noisy.Count) [ERR] lines in log — review:" -ForegroundColor Yellow
    $noisy | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}

Write-Host "[smoke] OK — binary launched, ran for $TimeoutSeconds seconds, no fatal errors logged" -ForegroundColor Green
exit 0
