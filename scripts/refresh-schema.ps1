# refresh-schema.ps1 — refresh every bundled JSON schema that has an upstream.
#
# WHY THIS EXISTS
# ---------------
# The bundled schemas under src/AgentForge.Core/Assets/Schemas/ are the OFFLINE FALLBACK
# the runtime reads when the network does not answer.  Runtime priority is
# memory cache > HTTPS fetch (+ strip, + overlay) > bundled resource (+ strip, + overlay);
# there is no disk cache and no empty fallback.  See CLAUDE.md "Schema loading priority".
#
# So refreshing these files matters for every user who is offline, on a slow link, or behind
# something that blocks the fetch — and for the first three seconds of every launch, since
# the fetch has a short timeout by design.
#
# Consequence: if an upstream ships a new model id, hook trigger, or settings property and
# we don't refresh THESE files, the editor never surfaces it.
#
# This script is the hand-run path AND the engine behind the weekly drift-check workflow at
# .github/workflows/schema-refresh.yml.  The workflow runs it unchanged and opens a PR when
# the working tree differs; you can also run it locally (between releases, or whenever a
# missing field is reported by a user) and commit the diff as:
#
#     chore(schema): refresh bundled schemas from upstream
#
# USAGE
# -----
#     pwsh scripts/refresh-schema.ps1                       # refresh all
#     pwsh scripts/refresh-schema.ps1 -DryRun               # preview, write nothing
#     pwsh scripts/refresh-schema.ps1 -Only opencode-config # one schema by name
#
# Requires PowerShell 7+.  No external deps beyond Invoke-WebRequest (built-in).
#
# ----------------------------------------------------------------------------
# THREE THINGS THAT ARE NOT OBVIOUS
# ----------------------------------------------------------------------------
# 1. claude-desktop-config.json has NO upstream URL — its $id is a bare token, not a
#    resolvable URL.  It is hand-maintained in-repo and is deliberately absent from the
#    table below.  Adding it would mean inventing a URL.
#
# 2. ⛔⛔ EXTERNAL $refs ARE STRIPPED, AND THAT IS LOAD-BEARING.
#    Upstream opencode-config.json types four `model` properties with
#    "$ref": "https://models.dev/model-schema.json#/$defs/Model" alongside a plain
#    "type": "string".  Leaving it in makes schema evaluation throw a ref-resolution
#    failure through ValidateWorkspaceAsync -> SaveAsync for ANY config that sets a model,
#    and the restore path's evaluate guard does not catch that exception type.  Resolving it
#    instead would impose a 6,688-entry allow-list that rejects custom models.
#    So every download has the strip re-applied.  A refresh that forgot would ship a schema
#    that breaks saving — measured, not theorised.
#
#    The strip is TEXTUAL — it deletes the offending lines and preserves upstream's
#    formatting.  Parsing and re-serialising would reformat all ~1,300 lines and produce a
#    diff no reviewer could read.
#
#    ⭐ This script does NOT verify the stripped properties are still typed.  That is
#    BundledOpenCodeSchemaTests.StrippingTheRefLeftTheModelPropertiesTyped, which checks all
#    four sites by JSON pointer.  One guard, in the layer that can assert precisely; the
#    script strips and validates that the result is still JSON with no http $ref left.
#
# 3. ⚠ COMPARISON IS LINE-ENDING NORMALISED, and the old byte-hash was wrong on Windows.
#    .gitattributes sets `* text=auto`, so a Windows checkout holds CRLF while every
#    download is LF.  claude-code-settings.json is 4,260 bytes larger on disk than upstream
#    for exactly that reason — 4,260 CRLF against 4,260 LF, byte-identical once normalised.
#    A raw hash compare therefore reported drift on every local run and -DryRun always
#    claimed a 4,260-line change.  CI never saw it (Linux checkout is LF), which is how it
#    survived.
#
# ----------------------------------------------------------------------------
# Hand-curated additions live in sibling overlay files
# ----------------------------------------------------------------------------
# Hand-curated additions live in `*.overlay.json` beside each schema, applied at load time
# by SchemaRegistry via RFC 7396 JSON Merge Patch.  This script only touches the BASE files
# — an overlay is NEVER affected by a refresh, so edits there persist and this script is
# idempotent for their contents.
#
# Today claude-code-settings.overlay.json carries `model.default`, `model.examples`, and an
# enriched `model.description` because upstream schemastore.org omits them (the model alias
# list churns faster than the schema does).  If a future upstream carries `examples`
# natively, delete the matching key from the overlay — the merge then surfaces upstream's
# value unchanged.
# ----------------------------------------------------------------------------

[CmdletBinding()]
param(
    [switch]$DryRun,
    [string[]]$Only
)

$ErrorActionPreference = 'Stop'

# Anchor on the script's location so the relative target paths work regardless of cwd
# (CI / nested shells / etc.).
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir '..')
$SchemaDir = Join-Path $RepoRoot 'src/AgentForge.Core/Assets/Schemas'

# ── The table ────────────────────────────────────────────────────────────────
# Name is what -Only matches and what the summary prints. Keep it equal to the file's
# base name so a reader never has to map between two vocabularies.
$Schemas = @(
    @{ Name = 'claude-code-settings'; File = 'claude-code-settings.json'; Url = 'https://json.schemastore.org/claude-code-settings.json' }
    @{ Name = 'opencode-config'; File = 'opencode-config.json'; Url = 'https://opencode.ai/config.json' }
    @{ Name = 'opencode-tui'; File = 'opencode-tui.json'; Url = 'https://opencode.ai/tui.json' }
)

if ($Only) {
    $known = @($Schemas | ForEach-Object { $_.Name })
    $unknown = @($Only | Where-Object { $known -notcontains $_ })
    if ($unknown.Count -gt 0) {
        # Write-Host, not Write-Error: $ErrorActionPreference='Stop' makes Write-Error THROW,
        # so the script would die with exit 1 and a stack trace before reaching the exit code
        # this usage error is documented to return. Measured.
        Write-Host ("Unknown schema name(s): " + ($unknown -join ', ')) -ForegroundColor Red
        Write-Host ("Known: " + ($known -join ', '))
        exit 2
    }
    $Schemas = @($Schemas | Where-Object { $Only -contains $_.Name })
}

# ── Helpers ──────────────────────────────────────────────────────────────────

# Normalise to LF so a CRLF working copy compares equal to an LF download. See note 3.
function Get-NormalisedText {
    param([string]$Path)
    $raw = [System.IO.File]::ReadAllText($Path)
    return $raw.Replace("`r`n", "`n")
}

<#
    Delete every line that is an http(s) $ref, fixing up the JSON as we go.

    Two cases, and getting them backwards produces invalid JSON:
      * the $ref line ends with a comma  -> siblings follow, just drop the line
      * it does not                      -> $ref was the LAST key, so the PRECEDING
                                            line's trailing comma must go too
    All four sites in today's opencode-config.json are the second case.
#>
function Remove-ExternalRefLine {
    param([string]$Text)

    $lines = $Text.Split("`n")
    $kept = [System.Collections.Generic.List[string]]::new()
    $removed = 0

    foreach ($line in $lines) {
        if ($line -notmatch '^\s*"\$ref"\s*:\s*"https?://') {
            $kept.Add($line)
            continue
        }

        $removed++

        if ($line.TrimEnd().EndsWith(',')) {
            continue
        }

        # Walk back over blank lines to the last real one and drop its trailing comma.
        for ($j = $kept.Count - 1; $j -ge 0; $j--) {
            if ($kept[$j].Trim().Length -eq 0) { continue }
            $trimmed = $kept[$j].TrimEnd()
            if ($trimmed.EndsWith(',')) {
                $kept[$j] = $trimmed.Substring(0, $trimmed.Length - 1)
            }
            break
        }
    }

    return [pscustomobject]@{ Text = ($kept -join "`n"); Removed = $removed }
}

# Count lines the way awk counts records, so the shell twin prints the same number:
# "a`nb`n" is two lines, not three. A raw Split leaves a trailing empty segment.
function Get-LineCount {
    param([string]$Text)
    $parts = $Text.Split("`n")
    if ($parts.Count -gt 0 -and $parts[$parts.Count - 1] -eq '') {
        return $parts.Count - 1
    }
    return $parts.Count
}

function Test-JsonText {
    param([string]$Text)
    try {
        $null = $Text | ConvertFrom-Json -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

# ── Run ──────────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host ('Refreshing ' + $Schemas.Count + ' bundled schema(s)') -ForegroundColor Cyan
Write-Host ('  target dir : ' + $SchemaDir)
if ($DryRun) {
    Write-Host '  mode       : DRY RUN (no files will be written)' -ForegroundColor Yellow
}
Write-Host ''

$changed = [System.Collections.Generic.List[string]]::new()
$failed = [System.Collections.Generic.List[string]]::new()

foreach ($schema in $Schemas) {
    $targetPath = Join-Path $SchemaDir $schema.File
    Write-Host ('-- ' + $schema.Name) -ForegroundColor Cyan
    Write-Host ('   upstream : ' + $schema.Url)

    if (-not (Test-Path $targetPath)) {
        Write-Host ('   ERROR: no bundled copy at ' + $targetPath + ' - wrong repo root?') -ForegroundColor Red
        $failed.Add($schema.Name)
        continue
    }

    $tempPath = Join-Path ([System.IO.Path]::GetTempPath()) (
        $schema.Name + '-' + [Guid]::NewGuid().ToString('N') + '.json.tmp')

    try {
        try {
            # -UseBasicParsing avoids the legacy IE-engine init removed in PS 7.
            # ProgressPreference silences the progress bar, which is slow on small files.
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $schema.Url -OutFile $tempPath -UseBasicParsing -ErrorAction Stop | Out-Null
        } catch {
            Write-Host ('   ERROR: download failed - ' + $_.Exception.Message) -ForegroundColor Red
            $failed.Add($schema.Name)
            continue
        }

        $downloaded = Get-NormalisedText $tempPath

        if (-not (Test-JsonText $downloaded)) {
            Write-Host '   ERROR: downloaded file is not valid JSON' -ForegroundColor Red
            Write-Host ('   first 200 chars: ' + $downloaded.Substring(0, [Math]::Min(200, $downloaded.Length)))
            $failed.Add($schema.Name)
            continue
        }

        # ── The strip. See note 2. ──
        $strip = Remove-ExternalRefLine $downloaded
        $candidate = $strip.Text

        if ($strip.Removed -gt 0) {
            Write-Host ('   stripped ' + $strip.Removed + ' external $ref line(s)') -ForegroundColor Yellow

            if (-not (Test-JsonText $candidate)) {
                Write-Host '   ERROR: the strip produced invalid JSON - upstream shape changed.' -ForegroundColor Red
                Write-Host '          Inspect the download by hand; do NOT commit this.' -ForegroundColor Red
                $failed.Add($schema.Name)
                continue
            }
        }

        if ($candidate -match '"\$ref"\s*:\s*"https?://') {
            Write-Host '   ERROR: an http $ref survived the strip.' -ForegroundColor Red
            Write-Host '          A bundled schema must resolve offline; see note 2.' -ForegroundColor Red
            $failed.Add($schema.Name)
            continue
        }

        # ── Compare, normalised. See note 3. ──
        $current = Get-NormalisedText $targetPath

        if ($current -eq $candidate) {
            $lineCount = Get-LineCount $current
            Write-Host ('   already up to date (' + $lineCount + ' lines)') -ForegroundColor Green
            continue
        }

        $oldLines = Get-LineCount $current
        $newLines = Get-LineCount $candidate
        Write-Host ('   CHANGED: lines ' + $oldLines + ' -> ' + $newLines +
            '  (delta ' + ($newLines - $oldLines) + ')') -ForegroundColor Yellow

        if ($DryRun) {
            Write-Host '   dry run - not written' -ForegroundColor Yellow
            $changed.Add($schema.Name)
            continue
        }

        # Write LF explicitly. Git normalises on `add` per .gitattributes, and writing the
        # platform default here would reintroduce exactly the CRLF/LF confusion note 3 is about.
        [System.IO.File]::WriteAllText($targetPath, $candidate)
        Write-Host '   written' -ForegroundColor Green
        $changed.Add($schema.Name)
    } finally {
        if (Test-Path $tempPath) {
            Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
        }
    }
}

# ── Summary ──────────────────────────────────────────────────────────────────

Write-Host ''
if ($failed.Count -gt 0) {
    Write-Host ('FAILED: ' + ($failed -join ', ')) -ForegroundColor Red
    Write-Host 'Nothing was written for those. Fix the cause before committing anything else.'
    exit 1
}

if ($changed.Count -eq 0) {
    Write-Host 'All bundled schemas match upstream. Nothing to do.' -ForegroundColor Green
    exit 0
}

Write-Host ('CHANGED: ' + ($changed -join ', ')) -ForegroundColor Yellow
Write-Host ''
Write-Host 'Note: ' -ForegroundColor Cyan -NoNewline
Write-Host 'the sibling *.overlay.json files were NOT touched; they are merged onto'
Write-Host '      whichever base wins at load time via RFC 7396 JSON Merge Patch.'
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host '  1. dotnet build ClaudeForge.slnx -c Debug     # the schemas still parse + bundle'
Write-Host '  2. dotnet test ClaudeForge.slnx -c Debug      # BundledOpenCodeSchemaTests is the'
Write-Host '                                                #   guard on the strip staying typed'
Write-Host '  3. git diff -- src/AgentForge.Core/Assets/Schemas/'
Write-Host '  4. commit as: chore(schema): refresh bundled schemas from upstream'
Write-Host ''
exit 0
