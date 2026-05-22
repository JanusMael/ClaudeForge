# refresh-schema.ps1 — manually refresh the bundled Claude Code JSON schema.
#
# WHY THIS EXISTS
# ---------------
# The bundled schema at src/ClaudeForge.Core/Assets/Schemas/claude-code-settings.json
# is the AUTHORITATIVE source the runtime reads — even when the app's HTTP refresh
# downloads a newer copy into ~/.claude/cache/schemas/, the runtime priority
# (memory cache > bundled embedded > disk cache > HTTP fetch > empty fallback)
# means the bundled file wins.  See CLAUDE.md "Schema loading priority".
#
# Consequence: if Anthropic ships a new model id, hook trigger, or settings
# property and we don't refresh THIS file, the editor never surfaces it.
#
# This script is the hand-run path AND the engine behind the weekly
# drift-check workflow at .github/workflows/schema-refresh.yml.  The
# workflow runs this script unchanged and opens a PR if the working
# tree differs from the bundled copy; you can also run it locally
# (between releases, or whenever a missing field is reported by a user)
# and commit the diff as:
#
#     chore: refresh bundled claude-code-settings.json from schemastore.org
#
# USAGE
# -----
#     pwsh scripts/refresh-schema.ps1            # apply
#     pwsh scripts/refresh-schema.ps1 -DryRun    # preview the diff, do not write
#
# Works on Windows PowerShell 5.1+ and PowerShell Core 7+.  No external deps
# beyond Invoke-WebRequest (built-in).
#
# claude-desktop-config.json has no upstream URL ($id is a bare token, not a
# resolvable URL).  It is hand-maintained in-repo.  This script only refreshes
# claude-code-settings.json.
#
# ----------------------------------------------------------------------------
# Hand-curated additions live in a sibling overlay file
# ----------------------------------------------------------------------------
# Hand-curated additions to the bundled schema live in a separate file:
# `claude-code-settings.overlay.json`, applied at load time by
# SchemaRegistry via RFC 7396 JSON Merge Patch.  This refresh script
# only touches `claude-code-settings.json` — the overlay is NEVER affected
# by a refresh.  Edits there persist across refreshes; this script is
# idempotent for the overlay's contents.
#
# Today the overlay carries `model.default`, `model.examples`, and an
# enriched `model.description` because upstream schemastore.org omits
# them (the model alias list churns faster than the schema does).  If a
# future upstream schema carries `examples` natively, simply delete the
# matching key from the overlay — the merge will then surface upstream's
# value unchanged.
# ----------------------------------------------------------------------------

[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# Anchor on the script's location so the relative target path works regardless
# of cwd (CI / nested shells / etc.).
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot    = Resolve-Path (Join-Path $ScriptDir '..')
$TargetPath  = Join-Path $RepoRoot 'src/ClaudeForge.Core/Assets/Schemas/claude-code-settings.json'
$UpstreamUrl = 'https://json.schemastore.org/claude-code-settings.json'

Write-Host ""
Write-Host "Refreshing Claude Code schema" -ForegroundColor Cyan
Write-Host "  upstream : $UpstreamUrl"
Write-Host "  target   : $TargetPath"
if ($DryRun) {
    Write-Host "  mode     : DRY RUN (no files will be written)" -ForegroundColor Yellow
}
Write-Host ""

# ---------------------------------------------------------------------------
# 1. Download the upstream schema to a temp file (atomic-write pattern).
# ---------------------------------------------------------------------------
$TempPath = Join-Path ([System.IO.Path]::GetTempPath()) `
    ("claude-code-settings-{0}.json.tmp" -f ([Guid]::NewGuid().ToString('N')))

try {
    # -UseBasicParsing avoids the legacy IE-engine init that's removed in PS 7.
    # ProgressPreference SilentlyContinue suppresses the giant progress bar
    # that's slow on small downloads.
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $UpstreamUrl `
                      -OutFile $TempPath `
                      -UseBasicParsing `
                      -ErrorAction Stop | Out-Null
} catch {
    Write-Error "Failed to download schema from $UpstreamUrl : $_"
    if (Test-Path $TempPath) { Remove-Item $TempPath -Force -ErrorAction SilentlyContinue }
    exit 1
}

# ---------------------------------------------------------------------------
# 2. Verify the download is valid JSON before touching the target file.
# ---------------------------------------------------------------------------
try {
    $null = Get-Content -Raw -LiteralPath $TempPath | ConvertFrom-Json -ErrorAction Stop
} catch {
    Write-Error "Downloaded file is not valid JSON: $_"
    Remove-Item $TempPath -Force -ErrorAction SilentlyContinue
    exit 1
}

$NewBytes = (Get-Item $TempPath).Length
$NewLines = (Get-Content $TempPath).Length

# ---------------------------------------------------------------------------
# 3. Compare against the current bundled copy.
# ---------------------------------------------------------------------------
if (-not (Test-Path $TargetPath)) {
    Write-Error "Target schema file not found at $TargetPath — wrong repo root?"
    Remove-Item $TempPath -Force -ErrorAction SilentlyContinue
    exit 1
}

$OldBytes = (Get-Item $TargetPath).Length
$OldLines = (Get-Content $TargetPath).Length

# Byte-identical short-circuit.
$OldHash = (Get-FileHash -LiteralPath $TargetPath -Algorithm SHA256).Hash
$NewHash = (Get-FileHash -LiteralPath $TempPath   -Algorithm SHA256).Hash

if ($OldHash -eq $NewHash) {
    Write-Host "Already up to date ($OldLines lines, $OldBytes bytes)." -ForegroundColor Green
    Remove-Item $TempPath -Force -ErrorAction SilentlyContinue
    exit 0
}

# ---------------------------------------------------------------------------
# 4. Show a brief diff summary so the operator sees the magnitude of change.
# ---------------------------------------------------------------------------
Write-Host "Change summary:"
Write-Host ("  lines : {0} -> {1}  (delta {2:+#;-#;0})" -f $OldLines, $NewLines, ($NewLines - $OldLines))
Write-Host ("  bytes : {0} -> {1}  (delta {2:+#;-#;0})" -f $OldBytes, $NewBytes, ($NewBytes - $OldBytes))
Write-Host ""

# If `git` is available, show the actual diff truncated to ~50 lines so the
# operator sees WHAT changed before committing.
if (Get-Command git -ErrorAction SilentlyContinue) {
    Write-Host "Diff (first 50 lines):" -ForegroundColor Cyan
    # --no-index treats the two files as standalone, ignoring git state.
    git --no-pager diff --no-index --color=always --stat -- $TargetPath $TempPath 2>$null
    Write-Host ""
    Write-Host "Full diff:" -ForegroundColor Cyan
    git --no-pager diff --no-index --color=always -- $TargetPath $TempPath 2>$null `
        | Select-Object -First 50
    Write-Host ""
}

# ---------------------------------------------------------------------------
# 5. Write through (or stop on dry-run).
# ---------------------------------------------------------------------------
if ($DryRun) {
    Write-Host "Dry run — target file NOT modified.  Re-run without -DryRun to apply." -ForegroundColor Yellow
    Remove-Item $TempPath -Force -ErrorAction SilentlyContinue
    exit 0
}

# Atomic-ish replace: copy temp over target.  Test-Path / Remove-Item / Copy
# pattern rather than Move because Move-Item across drives requires explicit
# -Force and has surprising semantics on Windows when target exists.
Copy-Item -LiteralPath $TempPath -Destination $TargetPath -Force
Remove-Item $TempPath -Force -ErrorAction SilentlyContinue

Write-Host "Bundled schema updated." -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# 6. Note about the sibling overlay file.
# ---------------------------------------------------------------------------
Write-Host "Note: " -ForegroundColor Cyan -NoNewline
Write-Host "hand-curated additions live in"
Write-Host "      src/ClaudeForge.Core/Assets/Schemas/claude-code-settings.overlay.json"
Write-Host "      and are applied at load time via RFC 7396 JSON Merge Patch."
Write-Host "      This refresh did NOT touch them; they will surface in the merged"
Write-Host "      runtime schema unchanged."
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. dotnet build              # verify the refreshed schema still parses + bundles."
Write-Host "  2. dotnet test               # verify dependent tests still pass."
Write-Host "  3. git diff -- $(Resolve-Path $TargetPath | Resolve-Path -Relative)"
Write-Host "  4. git add + commit with: 'chore: refresh bundled claude-code-settings.json from schemastore.org'"
Write-Host ""
