<#
.SYNOPSIS
    Publishes the ClaudeForge self-contained binary for a single RID.

.DESCRIPTION
    The single-RID worker behind the split publish workflow:

        publish.ps1                     (orchestrator — prompts per RID)
            ├── Publish-Rid.ps1         (THIS FILE — does the work)
            ├── publish-win-x64.ps1     (thin wrapper → Publish-Rid.ps1 -Rid win-x64 -Clean)
            ├── publish-win-arm64.ps1
            ├── publish-linux-x64.ps1
            ├── publish-linux-arm64.ps1
            ├── publish-osx-x64.ps1
            └── publish-osx-arm64.ps1

    All scripts live under src/publish/.

    Each invocation:
      1. Optionally wipes bin/obj across the src tree (see -Clean).
      2. Runs `dotnet publish -c Release -r <rid> --self-contained true`
         with output tee'd to a per-RID log under dist/logs/.
      3. Scans the log for `warning IL\d+` lines (ILLink diagnostics).
      4. Zips the published folder into dist/<project>-<rid>.zip.
      5. Emits a structured PSCustomObject to the pipeline so orchestrators
         can aggregate warnings across RIDs without re-parsing the log.

.PARAMETER Rid
    The .NET runtime identifier to publish for. Must be one of the RIDs
    declared in ClaudeForge.csproj's <RuntimeIdentifiers>.

.PARAMETER Clean
    When specified, wipes every bin/ and obj/ directory under the src tree
    before publishing. The manual wipe is used (not `dotnet clean`) because
    self-contained publishes leave project.assets.json in a per-RID shape
    that triggers NETSDK1047 on subsequent `dotnet clean` runs — see the
    block comment in publish.ps1 for the full explanation. The target RID's
    existing zip and staging folder under dist/ are also removed so the new
    build lands on a clean slate, but zips from OTHER RIDs are preserved —
    standalone per-RID invocations don't clobber sibling outputs.

.PARAMETER DistFolder
    Absolute path where the zip output should land. Defaults to
    `<srcRoot>/dist` (one level above this script) so standalone invocations
    and orchestrator invocations converge on the same layout.

.OUTPUTS
    PSCustomObject with properties:
      Rid          — the RID that was built
      ExitCode     — the exit code of `dotnet publish` (0 on success)
      LogPath      — absolute path to the per-RID publish log
      ArchivePath  — absolute path to the produced archive (if publish succeeded):
                     .zip for Windows RIDs, .tar.gz for Linux and macOS RIDs
      WarningCount — number of `warning IL\d+` lines matched in the log

.EXAMPLE
    pwsh src/publish/Publish-Rid.ps1 -Rid win-x64 -Clean

.EXAMPLE
    pwsh src/publish/Publish-Rid.ps1 -Rid linux-arm64 -Clean:$false
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string] $Rid,

    [switch] $Clean,

    [string] $DistFolder
)

$ErrorActionPreference = 'Stop'

# Suppress the built-in cmdlet progress UI ("Removing…" / "Searching…") that
# Remove-Item -Recurse and Get-ChildItem -Recurse emit when walking a large
# bin/obj tree. The progress bar uses VT escape sequences that IDE output
# panes (Rider, VS Code's Output window) don't render — they overwrite or
# eclipse the dotnet publish lines we actually want to read. Keeping it
# silent lets the build log scroll naturally in any host.
$ProgressPreference = 'SilentlyContinue'

# $PSScriptRoot is src/publish/ — the src/ root (where the csproj lives) is one level up.
$srcRoot     = Split-Path $PSScriptRoot -Parent
$projectName = "./ClaudeForge/ClaudeForge.csproj"
$projectPath = Join-Path $srcRoot $projectName

# Default dist/log folders under src/ so repeated runs accumulate in one place.
if (-not $DistFolder) { $DistFolder = Join-Path $srcRoot "dist" }
$logFolder = Join-Path $DistFolder "logs"

New-Item -ItemType Directory -Path $DistFolder -Force | Out-Null
New-Item -ItemType Directory -Path $logFolder  -Force | Out-Null

# --- Optional clean ---------------------------------------------------------
# Wipe bin/obj across the src tree (not via `dotnet clean` — see NETSDK1047
# rationale in publish.ps1) AND drop any prior zip/staging for THIS RID so
# the new build starts fresh. Other RIDs' zips in dist/ are left alone.
if ($Clean) {
    Write-Host "[$Rid] Wiping bin/ and obj/ across src tree..." -ForegroundColor DarkGray
    $cleanTargets = Get-ChildItem -Path $srcRoot -Directory -Recurse -Force `
            -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' } |
        Select-Object -ExpandProperty FullName
    foreach ($dir in $cleanTargets) {
        Remove-Item -Recurse -Force -LiteralPath $dir -ErrorAction SilentlyContinue
    }

    $projectNameOnly = [System.IO.Path]::GetFileNameWithoutExtension($projectName)
    $priorZip = Join-Path $DistFolder "$projectNameOnly-$Rid.zip"
    if (Test-Path $priorZip) { Remove-Item -Force -LiteralPath $priorZip }
    $priorStaging = Join-Path $DistFolder $Rid
    if (Test-Path $priorStaging) { Remove-Item -Recurse -Force -LiteralPath $priorStaging }
}

# --- Publish ----------------------------------------------------------------
Write-Host "`n>>> Publishing for $Rid..." -ForegroundColor Yellow
$ridFolder = Join-Path $DistFolder $Rid
$logPath   = Join-Path $logFolder "publish-$Rid.log"

# Always wipe the staging folder before publishing so that leftover files
# from previous runs (e.g. the app's own logs/ directory written if the binary
# was launched from the staging folder) do not end up in the distribution zip.
# The -Clean path already removes this folder; the non-Clean path needs it too.
if (Test-Path $ridFolder) {
    Write-Host "[$Rid] Cleaning prior staging folder..." -ForegroundColor DarkGray
    Remove-Item -Recurse -Force -LiteralPath $ridFolder
}

# /p:IncrementalBuild=false forces a fresh compilation per RID so the
# trimmed output cannot accidentally reuse another RID's artifacts.
# /p:RunResxKeyGuard=false skips the dev/CI unused-resx-key guard
# (Directory.Build.targets) during publish: it compiles an inline
# RoslynCodeTaskFactory task that can intermittently fail under concurrent
# builds / temp-dir contention (e.g. when publishing from an IDE) and is not
# needed to produce the release binary. The publish stream is tee'd to a log
# file for the warning scan below; Tee-Object does not overwrite $LASTEXITCODE
# so we can still read the real `dotnet publish` exit code.
dotnet publish "$projectPath" `
    -c Release `
    -r $Rid `
    --output "$ridFolder" `
    --self-contained true `
    -p:IncrementalBuild=false `
    -p:BuildInParallel=false `
    -p:RunResxKeyGuard=false 2>&1 | Tee-Object -FilePath $logPath

$publishExit = $LASTEXITCODE

# Scan the log for any `warning IL<digits>` line — covers IL2026, IL2067,
# IL2070, IL2072, IL2075, IL2104, IL2007 etc. MSBuild's "Build succeeded
# with N warning(s)" summary line does not match this pattern.
$warningLines = Select-String -Path $logPath -Pattern 'warning IL\d+' -ErrorAction SilentlyContinue
$warningCount = if ($warningLines) { $warningLines.Count } else { 0 }
if ($warningCount -gt 0) {
    Write-Host ("[{0}] {1} ILLink warning line(s) detected in {2}." -f `
            $Rid, $warningCount, (Split-Path $logPath -Leaf)) -ForegroundColor Yellow
}

# --- Archive the staged output -----------------------------------------------
# Windows → .zip   (native Windows archive format; no Unix permissions needed)
# Linux   → .tar.gz (idiomatic; preserves execute bit so the binary is runnable)
# macOS   → .tar.gz (same; macOS Finder and tar both handle .tar.gz natively)
#
# NOTE: we do NOT use the Windows system tar.exe for Unix targets.  NTFS has no
# execute bit, so bsdtar (Windows built-in) would write -rw-r--r-- for the
# binary, requiring a manual `chmod +x` after extraction.  .NET's TarWriter
# (System.Formats.Tar, .NET 7+) lets us specify rwxr-xr-x explicitly.
#
# The publish output is already the correct set of distributable files — no
# post-publish filtering is needed in this script because the build system
# handles every exclusion at the MSBuild level (Directory.Build.targets).
$archivePath = $null
if ($publishExit -eq 0) {
    # ── macOS: bundle the Gatekeeper quarantine remover script ───────────────
    # ClaudeForge v1 ships UNSIGNED and UNNOTARIZED (no Apple Developer
    # Program membership for the open-source release).  On first launch,
    # macOS Gatekeeper refuses to run the binary AND every .dylib next to
    # it because of the `com.apple.quarantine` xattr the OS attaches to
    # any file downloaded from the internet.  The right-click → Open
    # dialog only clears the xattr from the binary you click, not from
    # the dozens of supporting .dylib files a self-contained .NET publish
    # produces — so the app then fails at runtime with cryptic "Library
    # not loaded: ... operation not permitted" errors.
    #
    # Bundling assets/macos/allow-app-to-run.sh into the .tar.gz lets the
    # user run a single `./allow-app-to-run.sh` (or `sudo` variant if
    # extracted into a system location) to recursively strip the xattr
    # from the entire extracted directory in one shot.  See the script's
    # header comments for the full WHY / SECURITY rationale.
    #
    # Done BEFORE archive creation so the script lands in the .tar.gz
    # alongside the binary; the existing $execMode = rwxr-xr-x branch
    # below applies to every staged file, so the script is marked
    # executable in the archive automatically.
    if ($Rid -like 'osx-*') {
        $allowScript = Join-Path $PSScriptRoot '../../assets/macos/allow-app-to-run.sh'
        if (Test-Path $allowScript) {
            Copy-Item -Path $allowScript -Destination $ridFolder
            Write-Host "[$Rid] Bundled allow-app-to-run.sh into staging" -ForegroundColor DarkGray
        } else {
            Write-Warning "[$Rid] allow-app-to-run.sh not found at $allowScript — Gatekeeper helper will be missing from archive"
        }
    }

    # ── Linux: bundle the desktop-integration helper ─────────────────────────
    # Wayland compositors (KWin, Mutter, COSMIC, Sway) do not honour
    # Avalonia's in-process Window.Icon — they look up app_id.desktop in
    # $XDG_DATA_DIRS/applications/ and resolve the Icon= field against the
    # system icon theme.  Without an installed .desktop file the launched
    # window shows a generic application placeholder in dock / Alt-Tab.
    #
    # Bundling assets/linux/linux-setup.sh + claudeforge.desktop +
    # the SVG icon source lets the user run a single `./linux-setup.sh`
    # to write a per-user .desktop entry (Exec= pointing at this
    # directory's binary) and install the SVG into hicolor/scalable/apps/.
    # See the script's header comments for the full WHY / WHAT IT WRITES
    # / UNINSTALL rationale.
    #
    # Done BEFORE archive creation so the assets land in the .tar.gz; the
    # existing $execMode = rwxr-xr-x branch below applies to every staged
    # file, so the script ships executable and the .desktop / .svg files
    # ship with harmless rwxr-xr-x (mode bits are immaterial for read-only
    # consumers like update-desktop-database).
    if ($Rid -like 'linux-*') {
        $linuxAssets = @(
            @{ Src = '../../assets/linux/linux-setup.sh';                Name = 'linux-setup.sh' }
            @{ Src = '../../assets/linux/claudeforge.desktop';           Name = 'claudeforge.desktop' }
            @{ Src = '../ClaudeForge/Resources/ClaudeForge.svg';         Name = 'claudeforge.svg' }
        )
        $missing = @()
        foreach ($asset in $linuxAssets) {
            $srcPath = Join-Path $PSScriptRoot $asset.Src
            if (Test-Path $srcPath) {
                Copy-Item -Path $srcPath -Destination (Join-Path $ridFolder $asset.Name)
            } else {
                $missing += $srcPath
            }
        }
        if ($missing.Count -gt 0) {
            Write-Warning "[$Rid] Linux desktop-integration assets missing: $($missing -join ', ')"
        } else {
            Write-Host "[$Rid] Bundled linux-setup.sh + .desktop + .svg into staging" -ForegroundColor DarkGray
        }
    }

    $isWindowsRid = $Rid -like 'win-*'
    $archiveExt   = if ($isWindowsRid) { '.zip' } else { '.tar.gz' }

    $projectNameOnly = [System.IO.Path]::GetFileNameWithoutExtension($projectName)
    $archivePath     = Join-Path $DistFolder "$projectNameOnly-$Rid$archiveExt"

    Write-Host ("[$Rid] Creating $archiveExt archive...") -ForegroundColor Gray

    try {
        if (Test-Path $archivePath) { Remove-Item -Force -LiteralPath $archivePath }

        if ($isWindowsRid) {
            # ── Windows: ZIP ─────────────────────────────────────────────────
            Add-Type -AssemblyName "System.IO.Compression.FileSystem"
            [System.IO.Compression.ZipFile]::CreateFromDirectory(
                $ridFolder,
                $archivePath,
                [System.IO.Compression.CompressionLevel]::Optimal,
                $false)
        }
        else {
            # ── Linux / macOS: TAR.GZ ────────────────────────────────────────
            # rwxr-xr-x (0755) — appropriate for an executable binary and safe
            # for any other files that might be present alongside it.
            Add-Type -AssemblyName "System.IO.Compression"   # GZipStream
            Add-Type -AssemblyName "System.Formats.Tar"      # TarWriter, PaxTarEntry

            $execMode = [System.IO.UnixFileMode]::UserRead    -bor
                        [System.IO.UnixFileMode]::UserWrite   -bor
                        [System.IO.UnixFileMode]::UserExecute -bor
                        [System.IO.UnixFileMode]::GroupRead   -bor
                        [System.IO.UnixFileMode]::GroupExecute -bor
                        [System.IO.UnixFileMode]::OtherRead   -bor
                        [System.IO.UnixFileMode]::OtherExecute

            $fileStream = [System.IO.File]::Create($archivePath)
            $gzStream   = [System.IO.Compression.GZipStream]::new(
                              $fileStream,
                              [System.IO.Compression.CompressionLevel]::Optimal)
            # TarWriter(leaveOpen=$false) owns $gzStream; disposing the writer
            # also flushes GZip and closes $fileStream.
            $tarWriter = [System.Formats.Tar.TarWriter]::new($gzStream)
            try {
                foreach ($file in Get-ChildItem -Path $ridFolder -Recurse -File) {
                    # POSIX tar requires forward-slash entry names.
                    $entryName = $file.FullName.Substring($ridFolder.Length).TrimStart(
                        [System.IO.Path]::DirectorySeparatorChar,
                        [System.IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')

                    $entry      = [System.Formats.Tar.PaxTarEntry]::new(
                                      [System.Formats.Tar.TarEntryType]::RegularFile,
                                      $entryName)
                    $entry.Mode = $execMode

                    $fs = [System.IO.File]::OpenRead($file.FullName)
                    try {
                        $entry.DataStream = $fs
                        $tarWriter.WriteEntry($entry)
                    }
                    finally { $fs.Dispose() }
                }
            }
            finally { $tarWriter.Dispose() }   # → flushes GZip → closes file
        }

        $fileCount = (Get-ChildItem -Path $ridFolder -Recurse -File).Count
        Write-Host "[$Rid] Created: $(Split-Path $archivePath -Leaf) ($fileCount file(s))" -ForegroundColor Cyan
    }
    catch {
        Write-Host "[$Rid] Failed to create archive: $($_.Exception.Message)" -ForegroundColor Red
        $archivePath = $null
    }

    # Cleanup the staging folder. We deliberately keep
    # obj/Release/net10.0/<rid>/linked/ around so Analyze-XamlClosures.ps1
    # can metadata-probe the post-trim assemblies after the run.
    if (Test-Path $ridFolder) {
        Remove-Item -Recurse -Force -LiteralPath $ridFolder
    }
}
else {
    Write-Host "[$Rid] `dotnet publish` exited with code $publishExit." -ForegroundColor Red
}

# Emit a structured result so orchestrators can aggregate without re-parsing.
[pscustomobject]@{
    Rid          = $Rid
    ExitCode     = $publishExit
    LogPath      = $logPath
    ArchivePath  = $archivePath
    WarningCount = $warningCount
}
