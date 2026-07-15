# validate-model-catalog.ps1 — structural + cross-relationship validation of the
# bundled model catalog.
#
# WHY THIS EXISTS
# ---------------
# Unlike the JSON schema (refreshed from schemastore.org by refresh-schema.ps1),
# the model catalog has NO upstream machine-readable source — the harvested
# Claude docs are prose, so the curated file
# `src/ClaudeForge.Core/Assets/ModelCatalog/model-catalog.json` IS the source of
# truth. This script is therefore a VALIDATION GATE, not a downloader: it asserts
# the curated data is internally consistent (every supported effort level is a
# real level, every alias resolves to a real model, every default effort is
# supported, etc.) so a hand edit can't ship a broken catalog.
#
# It is the engine behind .github/workflows/model-catalog-refresh.yml (which also
# runs the ModelCatalog unit tests) and is runnable locally:
#
#     pwsh scripts/validate-model-catalog.ps1
#
# The overlay (`model-catalog.overlay.json`) is the hand-curated, refresh-safe
# seam; this script validates that it is well-formed JSON but does not require
# it to carry any keys (it ships as `{}`).
#
# Exit code: 0 = valid, 1 = one or more problems (all are listed before exit).
# Read-only — never writes any file.
#
# Works on PowerShell 7+ (ConvertFrom-Json -AsHashtable).

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Resolve-Path (Join-Path $ScriptDir '..')
$Dir       = Join-Path $RepoRoot 'src/ClaudeForge.Core/Assets/ModelCatalog'
$Catalog   = Join-Path $Dir 'model-catalog.json'
$Schema    = Join-Path $Dir 'model-catalog.schema.json'
$Overlay   = Join-Path $Dir 'model-catalog.overlay.json'

Write-Host ""
Write-Host "Validating model catalog" -ForegroundColor Cyan
Write-Host "  catalog : $Catalog"
Write-Host ""

$problems = New-Object System.Collections.Generic.List[string]

function Read-Json([string]$path) {
    if (-not (Test-Path $path)) {
        $script:problems.Add("Missing file: $path")
        return $null
    }
    try {
        return Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -ErrorAction Stop
    } catch {
        $script:problems.Add("Invalid JSON in $([System.IO.Path]::GetFileName($path)): $_")
        return $null
    }
}

$cat = Read-Json $Catalog
$null = Read-Json $Schema    # must parse
$null = Read-Json $Overlay   # must parse (ships as {})

# Hard-fail on an empty / whitespace / literal-null catalog. Get-Content of an
# empty file returns $null and `$null | ConvertFrom-Json` returns $null WITHOUT
# throwing, so without this the gate would print "valid" and exit 0.
if ($null -eq $cat) {
    $problems.Add("Catalog is empty, whitespace, or not a JSON object.")
}
elseif ($cat -isnot [pscustomobject]) {
    $problems.Add("Catalog root is not a JSON object.")
}

# Case-insensitive uniqueness, matching the C# loader's OrdinalIgnoreCase keying.
function Test-UniqueCI([string[]]$ids) {
    $lower = @($ids | ForEach-Object { $_.ToLowerInvariant() })
    return ($lower | Select-Object -Unique).Count -eq $lower.Count
}

if ($cat -is [pscustomobject]) {
    foreach ($key in 'schemaVersion', 'models', 'aliases', 'effortLevels', 'defaultModes') {
        if ($null -eq $cat.$key) { $problems.Add("Catalog missing required top-level key '$key'.") }
    }

    $effortIds = @($cat.effortLevels | ForEach-Object { $_.id })
    $modelIds  = @($cat.models | ForEach-Object { $_.id })

    # effortLevels: ids unique (case-insensitive).
    if (-not (Test-UniqueCI $effortIds)) {
        $problems.Add("effortLevels contains duplicate ids (case-insensitive).")
    }

    # models: ids unique (Resolve uses FirstOrDefault, so a dup silently shadows).
    if (-not (Test-UniqueCI $modelIds)) {
        $problems.Add("models contains duplicate ids (case-insensitive).")
    }

    # models: structural + cross-relationship.
    foreach ($m in $cat.models) {
        $id = $m.id
        if ([string]::IsNullOrWhiteSpace($id)) { $problems.Add("A model entry has an empty id."); continue }
        if ([string]::IsNullOrWhiteSpace($m.label)) { $problems.Add("Model '$id' has an empty label.") }

        foreach ($lvl in @($m.supportedEffortLevels)) {
            if ($effortIds -notcontains $lvl) {
                $problems.Add("Model '$id' lists unsupported effort level '$lvl' (not in effortLevels).")
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($m.defaultEffortLevel)) {
            if (@($m.supportedEffortLevels) -notcontains $m.defaultEffortLevel) {
                $problems.Add("Model '$id' defaultEffortLevel '$($m.defaultEffortLevel)' is not in its supportedEffortLevels.")
            }
        }
    }

    # aliases: every value resolves to a real model id.
    foreach ($p in $cat.aliases.PSObject.Properties) {
        if ($modelIds -notcontains $p.Value) {
            $problems.Add("Alias '$($p.Name)' points at unknown model id '$($p.Value)'.")
        }
    }

    # Cross-check: each model's `alias` field must agree with the aliases{} map.
    foreach ($m in $cat.models) {
        if ([string]::IsNullOrWhiteSpace($m.alias)) { continue }
        $mapped = ($cat.aliases.PSObject.Properties | Where-Object { $_.Name -eq $m.alias } | Select-Object -First 1).Value
        if ($null -eq $mapped) {
            $problems.Add("Model '$($m.id)' declares alias '$($m.alias)' but it is missing from the aliases{} map.")
        }
        elseif ($mapped -ne $m.id) {
            $problems.Add("Model '$($m.id)' alias '$($m.alias)' maps to '$mapped' in aliases{}, not '$($m.id)'.")
        }
    }

    # defaultModes: ids present + unique (case-insensitive).
    $modeIds = @($cat.defaultModes | ForEach-Object { $_.id })
    foreach ($mid in $modeIds) {
        if ([string]::IsNullOrWhiteSpace($mid)) { $problems.Add("A defaultModes entry has an empty id.") }
    }
    if (-not (Test-UniqueCI $modeIds)) {
        $problems.Add("defaultModes contains duplicate ids (case-insensitive).")
    }
}

if ($problems.Count -gt 0) {
    Write-Host "Validation FAILED ($($problems.Count) problem(s)):" -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
    Write-Host ""
    exit 1
}

Write-Host "Model catalog is valid." -ForegroundColor Green
Write-Host ("  models={0}  effortLevels={1}  defaultModes={2}  aliases={3}" -f `
    @($cat.models).Count, @($cat.effortLevels).Count, @($cat.defaultModes).Count, @($cat.aliases.PSObject.Properties).Count)
Write-Host ""
exit 0
