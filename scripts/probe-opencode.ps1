#Requires -Version 7.0
<#
.SYNOPSIS
    Measures a real OpenCode installation and writes a committable JSON snapshot.

.DESCRIPTION
    Phase 16 of docs/OPENCODEFORGE-PLAN.md re-validates this project's assumptions against an
    install with accumulated usage. Everything the plan recorded before was measured against an
    install whose data, state and cache directories were all 0 bytes, which is enough to build
    against and not enough to trust for anything derived from accumulated state.

    Running this by hand each time makes a re-check a re-investigation. Committing its output
    makes a re-check a `git diff` — and the same snapshot is the field diagnostic when a user
    reports something the app got wrong.

    THREE INVARIANTS, all of them about the fact that this output is committed to a public repo.

    1. READ-ONLY. The SQLite database is never opened in place. Opening a database whose -wal is
       present CHECKPOINTS it, which rewrites the user's file; so the three files are copied to a
       temporary directory and every query runs against the copy. Sizes are read BEFORE copying,
       because the copy loses its -wal the moment sqlite touches it.

    2. NO VALUES, ONLY SHAPE. The only queries issued against the database are COUNT(*), PRAGMA,
       and reads of sqlite_master. Nothing selects a row. That is deliberate and not merely
       cautious: `credential` is a real table in OpenCode's schema, with a `value text NOT NULL`
       column, so a probe that dumped rows would commit the maintainer's provider tokens.

    3. REDACTED PATHS. The home directory renders as `~` and the temp directory as `<temp>`, so
       the snapshot neither leaks a username nor differs between two machines that are in the
       same state. Lock metadata carries a token, a pid and a hostname; only its KEY NAMES are
       emitted.

.PARAMETER ProbeOutputPath
    Where to write the snapshot. Defaults to docs/opencode-install-probe.json beside the repo.

.PARAMETER ProbeQuiet
    Suppress the human-readable summary; write the file only.

.NOTES
    Portable by design: pwsh 7 runs on all three platforms this project ships to, so there is no
    .sh twin to drift out of parity. `opencode` and `sqlite3` are both OPTIONAL — an absent tool
    is recorded as a fact in the snapshot rather than failing the run, because "the maintainer
    has no sqlite3" and "the database has no rows" must never look alike in a diff.

    Set PROBE_OPENCODE_NOEXEC=1 to dot-source this for its functions without running it.
#>
[CmdletBinding()]
param(
    [string] $ProbeOutputPath,
    [switch] $ProbeQuiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 2 added database.existedBeforeProbe / .createdByThisProbe, and moved the CLI calls ahead of
# the filesystem measurement so a v1 snapshot's root sizes cannot be compared with a v2's.
$script:SnapshotSchemaVersion = 2

# ---------------------------------------------------------------- redaction

function Get-ProbeRedactedPath {
    <#  Renders an absolute path machine-independently: `~` for the profile, `<temp>` for the
        temp root, forward slashes throughout. Longest prefix first — on Windows the temp
        directory sits UNDER the profile, so testing the profile first would swallow it. #>
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $Path }

    # A GUID in a path is an opaque identifier — an agent session id, a sandbox id — that carries
    # no information a reader of this snapshot can use, and it defeats a guard that treats a bare
    # GUID as a leaked token. The maintainer's one `project` row points at exactly such a path.
    $normalized = ($Path -replace '\\', '/') -replace `
        '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', '<guid>'
    $comparison = if ($IsWindows) { 'OrdinalIgnoreCase' } else { 'Ordinal' }

    $prefixes = @(
        @{ Root = ([System.IO.Path]::GetTempPath()); Token = '<temp>' }
        @{ Root = $HOME;                             Token = '~' }
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Root) }

    foreach ($p in $prefixes) {
        $root = ($p.Root -replace '\\', '/').TrimEnd('/')
        if ($root -and $normalized.StartsWith($root, $comparison)) {
            return $p.Token + $normalized.Substring($root.Length)
        }
    }

    return $normalized
}

# ---------------------------------------------------------------- filesystem

function Measure-ProbeTree {
    <#  Bytes, file count, directory count and the newest write under a root. The newest write is
        the single cheapest growth signal in the whole snapshot: two runs with identical sizes and
        a moved timestamp mean churn, and identical timestamps mean the install was never used. #>
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return [ordered]@{ exists = $false; bytes = 0; files = 0; dirs = 0; newestWriteUtc = $null }
    }

    $files = @(Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue)
    $dirs = @(Get-ChildItem -LiteralPath $Path -Recurse -Force -Directory -ErrorAction SilentlyContinue)

    $bytes = 0
    if ($files.Count -gt 0) {
        $bytes = [int64](($files | Measure-Object -Property Length -Sum).Sum)
    }

    $newest = $null
    if ($files.Count -gt 0) {
        $newest = ($files | Sort-Object -Property LastWriteTimeUtc -Descending |
            Select-Object -First 1).LastWriteTimeUtc.ToString('o')
    }

    return [ordered]@{
        exists         = $true
        bytes          = $bytes
        files          = $files.Count
        dirs           = $dirs.Count
        newestWriteUtc = $newest
    }
}

function Get-ProbeChildBreakdown {
    <#  Per-top-level-child sizes. This is what inverted the plan's probe 4: the cache root was
        assumed to be mostly `bin/`, and a breakdown showed `bin/` empty and one 4 MB file. #>
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) { return @() }

    $children = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue |
        Sort-Object -Property Name)

    $result = @()
    foreach ($c in $children) {
        if ($c.PSIsContainer) {
            $m = Measure-ProbeTree -Path $c.FullName
            $result += [ordered]@{
                name = $c.Name; kind = 'dir'; bytes = $m.bytes; files = $m.files
            }
        }
        else {
            $result += [ordered]@{
                name = $c.Name; kind = 'file'; bytes = [int64]$c.Length; files = 1
            }
        }
    }

    return $result
}

# ---------------------------------------------------------------- tools

function Get-ProbeTool {
    <#  Resolves an optional external tool to a name/version/available triple. #>
    param([string] $Name, [string[]] $VersionArgs)

    $cmd = Get-Command -Name $Name -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if (-not $cmd) {
        return [ordered]@{ available = $false; version = $null }
    }

    $version = $null
    try {
        $raw = & $cmd.Source @VersionArgs 2>&1
        $version = (@($raw) | Where-Object { $_ } | Select-Object -First 1).ToString().Trim()
        # sqlite3 --version appends its 64-character source SHA. It is not a secret, but it is
        # indistinguishable from one to any guard worth having, and it tells a reader nothing.
        $version = $version -replace '\b[0-9a-fA-F]{32,}\b', '<sha>'
    }
    catch [System.ComponentModel.Win32Exception], [System.Management.Automation.ApplicationFailedException] {
        $version = $null
    }

    return [ordered]@{ available = $true; version = $version }
}

function Invoke-ProbeSqlite {
    <#  Runs one statement and returns parsed JSON rows, or @() when the statement yields nothing.
        SQL travels as an ARGUMENT, never through a shell, so nothing re-quotes it on the way. #>
    param([string] $Sqlite3, [string] $DatabasePath, [string] $Sql)

    $raw = & $Sqlite3 '-json' $DatabasePath $Sql 2>&1
    $exit = $LASTEXITCODE
    $text = (@($raw) -join "`n").Trim()

    # Exit code BEFORE emptiness: a statement that fails without printing anything is not the
    # same fact as a statement that legitimately matched no rows, and only one of them is fine.
    if ($exit -ne 0) { throw "sqlite3 exited $exit on '$Sql': $text" }
    if ([string]::IsNullOrWhiteSpace($text)) { return @() }

    return @($text | ConvertFrom-Json)
}

function Get-ProbeScalar {
    param([string] $Sqlite3, [string] $DatabasePath, [string] $Sql)

    $raw = & $Sqlite3 $DatabasePath $Sql 2>&1
    return ((@($raw) -join "`n").Trim())
}

# ---------------------------------------------------------------- database

function Get-ProbeTableCounts {
    <#  COUNT(*) per table. One process per table is wasteful and it is also the only form that
        survives a table this project has never heard of, which is the whole point of a probe. #>
    param([string] $Sqlite3, [string] $DatabasePath)

    $tables = Invoke-ProbeSqlite -Sqlite3 $Sqlite3 -DatabasePath $DatabasePath -Sql @'
SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;
'@

    $counts = [ordered]@{}
    foreach ($t in $tables) {
        $quoted = '"' + ($t.name -replace '"', '""') + '"'
        $counts[$t.name] = [int](Get-ProbeScalar -Sqlite3 $Sqlite3 -DatabasePath $DatabasePath `
                -Sql "SELECT COUNT(*) FROM $quoted;")
    }

    return $counts
}

function Get-ProbeSecretBearingTables {
    <#  Which tables hold secret material, and WHY each one qualified. Reported even at zero rows:
        the schema is the fact Phase 14's redaction requirement rests on, not the row count.

        ⛔ Matched on COLUMN names, not just table names. A first draft matched table names alone
        and reported `credential` — while `account` and `control_account`, which each carry
        `access_token` and `refresh_token`, qualified only by the accident of having "account" in
        the name. A table called `provider` with an `api_key` column would have been missed
        entirely, which is the case this exists to catch.

        `matchedOn` is recorded so the snapshot explains itself: a table flagged only by its name
        is a weaker signal than one flagged by a column, and a reader six months from now must not
        have to re-derive which happened.

        ⛔⛔ THE COLUMN PATTERN IS ANCHORED, and a loose one measured why that matters. A first
        pass at `token|secret|session[-_]?id` flagged TWELVE of the twenty tables: `session` for
        its `tokens_input`/`tokens_output` LLM USAGE COUNTERS, and seven more for a `session_id`
        FOREIGN KEY. A redaction list that is two-thirds false positives is worse than a short
        one — whoever implements Phase 14 either redacts foreign keys and breaks restore, or
        learns to disregard the list. Anchoring on whole segments leaves exactly the four tables
        that hold real secret material. #>
    param([string] $Sqlite3, [string] $DatabasePath, [string[]] $TableNames)

    # Whole-segment matches only. `(^|_)token$` deliberately does NOT match `tokens_input` (a
    # counter) or `token_expiry` (a timestamp).
    $columnPattern = '(^|_)((access|refresh|id|bearer)_token|token|secret|secrets|password|passwd|' +
    'api_?key|access_?key|private_?key|credential|credentials)$'

    # Table names only where the table IS a secret store by role. Deliberately not `account`:
    # `account` and `control_account` already qualify on their real token columns, whereas
    # `account_state` holds two active-id pointers and nothing else, and flagging it by family
    # resemblance is how a list stops being read.
    $tablePattern = '(^|_)(credential|credentials|auth)($|_)'

    $result = @()
    foreach ($name in $TableNames) {
        $cols = Invoke-ProbeSqlite -Sqlite3 $Sqlite3 -DatabasePath $DatabasePath `
            -Sql "SELECT name FROM pragma_table_info('$($name -replace "'", "''")') ORDER BY cid;"
        if ($cols.Count -eq 0) { continue }

        $columnNames = @($cols | ForEach-Object { $_.name })
        $matchedColumns = @($columnNames | Where-Object { $_ -match $columnPattern })
        $nameMatches = ($name -match $tablePattern)

        if ($matchedColumns.Count -eq 0 -and -not $nameMatches) { continue }

        $matchedOn = @()
        if ($matchedColumns.Count -gt 0) { $matchedOn += @($matchedColumns | ForEach-Object { 'column:' + $_ }) }
        if ($nameMatches) { $matchedOn += 'table-name' }

        $result += [ordered]@{
            table     = $name
            matchedOn = $matchedOn
            columns   = $columnNames
        }
    }

    return $result
}

function Get-ProbeDatabase {
    <#  Everything about opencode.db, measured on a COPY.

        The `walCarriedRows` control is the part worth keeping: a -wal larger than its database
        looks alarming, and the only way to tell "uncheckpointed user data" from "migration churn
        that was never checkpointed" is to count rows twice — once with the -wal alongside and
        once without — and compare. #>
    param([string] $Sqlite3Path, [string] $DataDirectory)

    $dbPath = Join-Path $DataDirectory 'opencode.db'
    if (-not (Test-Path -LiteralPath $dbPath)) {
        return [ordered]@{ present = $false }
    }

    # BEFORE any copy: sqlite folds the -wal into the copy and deletes it.
    $sizeOf = {
        param($p)
        if (Test-Path -LiteralPath $p) { [int64](Get-Item -LiteralPath $p).Length } else { $null }
    }

    $info = [ordered]@{
        present  = $true
        bytes    = & $sizeOf $dbPath
        walBytes = & $sizeOf "$dbPath-wal"
        shmBytes = & $sizeOf "$dbPath-shm"
        lastWriteUtc = (Get-Item -LiteralPath $dbPath).LastWriteTimeUtc.ToString('o')
    }

    if (-not $Sqlite3Path) {
        $info['inspected'] = $false
        $info['skippedBecause'] = 'sqlite3 is not on PATH'
        return $info
    }

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("probe-opencode-" + [guid]::NewGuid().ToString('N'))
    try {
        $withWal = Join-Path $work 'withwal'
        $dbOnly = Join-Path $work 'dbonly'
        New-Item -ItemType Directory -Path $withWal -Force | Out-Null
        New-Item -ItemType Directory -Path $dbOnly -Force | Out-Null

        foreach ($suffix in @('', '-wal', '-shm')) {
            $src = "$dbPath$suffix"
            if (Test-Path -LiteralPath $src) {
                Copy-Item -LiteralPath $src -Destination (Join-Path $withWal "opencode.db$suffix") -Force
            }
        }
        Copy-Item -LiteralPath $dbPath -Destination (Join-Path $dbOnly 'opencode.db') -Force

        $copyWithWal = Join-Path $withWal 'opencode.db'
        $copyDbOnly = Join-Path $dbOnly 'opencode.db'

        $info['inspected'] = $true
        $info['journalMode'] = Get-ProbeScalar $Sqlite3Path $copyWithWal 'PRAGMA journal_mode;'
        $info['pageSize'] = [int](Get-ProbeScalar $Sqlite3Path $copyWithWal 'PRAGMA page_size;')
        $info['pageCount'] = [int](Get-ProbeScalar $Sqlite3Path $copyWithWal 'PRAGMA page_count;')
        $info['freelistCount'] = [int](Get-ProbeScalar $Sqlite3Path $copyWithWal 'PRAGMA freelist_count;')
        $info['integrityCheck'] = Get-ProbeScalar $Sqlite3Path $copyWithWal 'PRAGMA integrity_check;'

        $counts = Get-ProbeTableCounts -Sqlite3 $Sqlite3Path -DatabasePath $copyWithWal
        $info['tableCount'] = $counts.Keys.Count
        $info['rowsByTable'] = $counts

        $countsNoWal = Get-ProbeTableCounts -Sqlite3 $Sqlite3Path -DatabasePath $copyDbOnly
        $differing = @($counts.Keys | Where-Object {
                (-not $countsNoWal.Contains($_)) -or ($countsNoWal[$_] -ne $counts[$_])
            })
        $info['walCarriedRows'] = ($differing.Count -gt 0)
        $info['walCarriedRowsIn'] = $differing

        # EVERY table is offered, not a name-filtered subset — the filtering is the callee's job
        # and it reads columns, which is the only way to catch a secret in an innocuous table.
        $info['secretBearingTables'] = @(Get-ProbeSecretBearingTables -Sqlite3 $Sqlite3Path `
                -DatabasePath $copyWithWal -TableNames @($counts.Keys))
    }
    finally {
        if (Test-Path -LiteralPath $work) {
            Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    return $info
}

# ---------------------------------------------------------------- locks

function Get-ProbeLocks {
    <#  Probe 3. The shape is the answer: OpenCode's lock is a DIRECTORY holding `heartbeat` and
        `meta.json`, i.e. a mkdir mutex, which is cooperative and cannot block another process's
        writes at the OS level. Recording isDirectory is therefore not pedantry — it is the
        measurement that decides whether "a lock held during our save" is a hazard at all. #>
    param([string] $StateDirectory)

    $locks = Join-Path $StateDirectory 'locks'
    if (-not (Test-Path -LiteralPath $locks)) {
        return [ordered]@{ present = $false; entries = @() }
    }

    $entries = @()
    foreach ($e in @(Get-ChildItem -LiteralPath $locks -Force | Sort-Object -Property Name)) {
        $entry = [ordered]@{
            nameShape   = ($e.Name -replace '^[0-9a-fA-F]{40}', '<sha1>')
            isDirectory = [bool]$e.PSIsContainer
        }

        if ($e.PSIsContainer) {
            $inner = @(Get-ChildItem -LiteralPath $e.FullName -Force | Sort-Object -Property Name)
            $entry['contains'] = @($inner | ForEach-Object { $_.Name })

            $meta = $inner | Where-Object { $_.Name -eq 'meta.json' } | Select-Object -First 1
            if ($meta) {
                # Key names only. The values are a token, a pid and a hostname.
                $parsed = Get-Content -LiteralPath $meta.FullName -Raw | ConvertFrom-Json
                $entry['metaKeys'] = @($parsed.PSObject.Properties.Name | Sort-Object)
                if ($parsed.PSObject.Properties.Name -contains 'createdAt') {
                    $entry['createdAtUtc'] = ([datetime] $parsed.createdAt).ToUniversalTime().ToString('o')
                }
            }
        }

        $entries += $entry
    }

    return [ordered]@{ present = $true; count = $entries.Count; entries = $entries }
}

# ---------------------------------------------------------------- opencode CLI

function Get-ProbePaths {
    <#  `opencode debug paths` prints `key<whitespace>value`. Parsed rather than re-derived
        because this project must not guess at the layout it is measuring. #>
    param([string] $OpenCodePath)

    if (-not $OpenCodePath) { return [ordered]@{} }

    $raw = & $OpenCodePath 'debug' 'paths' 2>&1
    if ($LASTEXITCODE -ne 0) { return [ordered]@{} }

    $map = [ordered]@{}
    foreach ($line in @($raw)) {
        $text = "$line".Trim()
        if (-not $text) { continue }
        $m = [regex]::Match($text, '^(?<k>\S+)\s+(?<v>.+)$')
        if ($m.Success) {
            $map[$m.Groups['k'].Value] = Get-ProbeRedactedPath $m.Groups['v'].Value
        }
    }

    return $map
}

function Get-ProbeScrap {
    <#  Probe 6. Emits per-project shape only, with the worktree redacted — the one entry on the
        maintainer's machine points into a deleted agent scratchpad, which is exactly the kind of
        detail that must not be mistaken for real usage in six months. #>
    param([string] $OpenCodePath)

    if (-not $OpenCodePath) { return [ordered]@{ available = $false } }

    $raw = & $OpenCodePath 'debug' 'scrap' 2>&1
    if ($LASTEXITCODE -ne 0) { return [ordered]@{ available = $false } }

    try {
        $parsed = @((@($raw) -join "`n") | ConvertFrom-Json)
    }
    catch [System.ArgumentException] {
        return [ordered]@{ available = $false }
    }

    $projects = @()
    foreach ($p in $parsed) {
        $projects += [ordered]@{
            id           = $p.id
            vcs          = $p.vcs
            worktree     = Get-ProbeRedactedPath $p.worktree
            sandboxCount = @($p.sandboxes).Count
        }
    }

    return [ordered]@{ available = $true; projectCount = $projects.Count; projects = $projects }
}

# ---------------------------------------------------------------- plugins

function Get-ProbePlugins {
    <#  Probe 5. The plan recorded "60 MB with one plugin"; what matters for a footprint page is
        the ratio, so the plugin count and the node_modules share are measured together. #>
    param([string] $ConfigDirectory)

    $pluginDir = Join-Path $ConfigDirectory 'plugins'
    $files = @()
    if (Test-Path -LiteralPath $pluginDir) {
        $files = @(Get-ChildItem -LiteralPath $pluginDir -Force -File | Sort-Object -Property Name |
            ForEach-Object { [ordered]@{ name = $_.Name; bytes = [int64]$_.Length } })
    }

    $deps = [ordered]@{}
    $packageJson = Join-Path $ConfigDirectory 'package.json'
    if (Test-Path -LiteralPath $packageJson) {
        $parsed = Get-Content -LiteralPath $packageJson -Raw | ConvertFrom-Json
        if ($parsed.PSObject.Properties.Name -contains 'dependencies') {
            foreach ($d in @($parsed.dependencies.PSObject.Properties | Sort-Object -Property Name)) {
                $deps[$d.Name] = $d.Value
            }
        }
    }

    $nodeModules = Measure-ProbeTree -Path (Join-Path $ConfigDirectory 'node_modules')
    $topLevel = 0
    $nmPath = Join-Path $ConfigDirectory 'node_modules'
    if (Test-Path -LiteralPath $nmPath) {
        $topLevel = @(Get-ChildItem -LiteralPath $nmPath -Force).Count
    }

    return [ordered]@{
        pluginFiles          = $files
        pluginFileCount      = $files.Count
        declaredDependencies = $deps
        nodeModules          = $nodeModules
        nodeModulesTopLevel  = $topLevel
    }
}

# ---------------------------------------------------------------- verdict

function Get-ProbeUsageVerdict {
    <#  The one field a re-check is really looking for. Phase 14 is blocked until an install has
        genuine session history, and "does it?" must be a computed boolean rather than a
        judgement someone re-forms from a wall of numbers. #>
    param($Database)

    $evidence = @()
    $used = $false

    # A database this probe just created is decisive evidence of NO usage, not weak evidence of
    # some. Stating it explicitly keeps a reader from mistaking a freshly-initialised schema for
    # an install that has been worked in.
    if ($Database.Contains('createdByThisProbe') -and $Database.createdByThisProbe) {
        $evidence += 'opencode.db did not exist until THIS PROBE created it (opencode debug paths initialises it)'
    }

    if (-not $Database.present) {
        $evidence += 'opencode.db is absent'
    }
    elseif (-not $Database.inspected) {
        $evidence += 'opencode.db present but not inspected (' + $Database.skippedBecause + ')'
    }
    else {
        foreach ($t in @('session', 'message', 'part', 'todo')) {
            if ($Database.rowsByTable.Contains($t)) {
                $n = $Database.rowsByTable[$t]
                $evidence += "$t rows = $n"
                if ($n -gt 0) { $used = $true }
            }
            else {
                $evidence += "$t table absent"
            }
        }
    }

    return [ordered]@{
        isUsedInstall = $used
        blocksPhase14 = (-not $used)
        evidence      = $evidence
    }
}

# ---------------------------------------------------------------- assembly

function New-ProbeSnapshot {
    $openCode = Get-ProbeTool -Name 'opencode' -VersionArgs @('--version')
    $sqlite = Get-ProbeTool -Name 'sqlite3' -VersionArgs @('--version')

    $openCodePath = $null
    if ($openCode.available) {
        $openCodePath = (Get-Command -Name 'opencode' -CommandType Application |
            Select-Object -First 1).Source
    }

    $sqlitePath = $null
    if ($sqlite.available) {
        $sqlitePath = (Get-Command -Name 'sqlite3' -CommandType Application |
            Select-Object -First 1).Source
    }

    # Resolved locally rather than from `debug paths`, which prints a static table: it reports the
    # DEFAULT config directory even when OPENCODE_CONFIG_DIR is set. See OpenCodePaths.cs.
    $configDir = if ($env:OPENCODE_CONFIG_DIR) { $env:OPENCODE_CONFIG_DIR }
    else { Join-Path (Join-Path $HOME '.config') 'opencode' }
    $dataDir = Join-Path (Join-Path (Join-Path $HOME '.local') 'share') 'opencode'
    $stateDir = Join-Path (Join-Path (Join-Path $HOME '.local') 'state') 'opencode'
    $cacheDir = Join-Path (Join-Path $HOME '.cache') 'opencode'

    # ⛔⛔ THE CLI CALLS RUN FIRST, AND THAT ORDERING IS A FIX FOR A REAL DEFECT.
    #
    # `opencode debug paths` CREATES opencode.db as a side effect. The first version of this
    # script measured the roots and THEN called the CLI from inside its return block, so on a
    # machine with no database yet it reported `data: 0 bytes, 0 files` and `present: false`
    # while leaving a freshly-created database behind — a probe that manufactures the state it
    # is measuring and then reports the state from before it did. It was invisible on the one
    # machine that had a database already, and on the machine that did not it produced the
    # headline conclusion "not a used install" about a directory it had just populated.
    #
    # So: note whether the database exists BEFORE anything invokes the CLI, make the calls, and
    # only then measure. The numbers are consistent with each other afterwards, and
    # `createdByThisProbe` makes the contamination a recorded fact instead of a silent one.
    $databasePath = Join-Path $dataDir 'opencode.db'
    $databaseExistedBefore = Test-Path -LiteralPath $databasePath

    $reportedPaths = Get-ProbePaths -OpenCodePath $openCodePath
    $scrap = Get-ProbeScrap -OpenCodePath $openCodePath

    $roots = [ordered]@{}
    foreach ($pair in @(
            @{ Role = 'config'; Path = $configDir }
            @{ Role = 'data'; Path = $dataDir }
            @{ Role = 'state'; Path = $stateDir }
            @{ Role = 'cache'; Path = $cacheDir }
        )) {
        $m = Measure-ProbeTree -Path $pair.Path
        $m['path'] = Get-ProbeRedactedPath $pair.Path
        # ⛔ @() at the ASSIGNMENT, not inside the function. `return $result` ENUMERATES, so a root
        # with exactly one child serialized as a bare object while every other root was an array —
        # `state` did precisely that, and a consumer diffing the two shapes would break on it.
        $m['children'] = @(Get-ProbeChildBreakdown -Path $pair.Path)
        $roots[$pair.Role] = $m
    }

    $database = Get-ProbeDatabase -Sqlite3Path $sqlitePath -DataDirectory $dataDir
    $database['existedBeforeProbe'] = $databaseExistedBefore
    $database['createdByThisProbe'] = ([bool]$database.present -and -not $databaseExistedBefore)

    return [ordered]@{
        schemaVersion   = $script:SnapshotSchemaVersion
        capturedAtUtc   = (Get-Date).ToUniversalTime().ToString('o')
        tooling         = [ordered]@{
            opencode      = $openCode
            sqlite3       = $sqlite
            powershell    = $PSVersionTable.PSVersion.ToString()
            platform      = if ($IsWindows) { 'Windows' } elseif ($IsMacOS) { 'macOS' } else { 'Linux' }
        }
        reportedPaths   = $reportedPaths
        roots           = $roots
        database        = $database
        locks           = Get-ProbeLocks -StateDirectory $stateDir
        plugins         = Get-ProbePlugins -ConfigDirectory $configDir
        scrap           = $scrap
        usage           = Get-ProbeUsageVerdict -Database $database
    }
}

function Write-ProbeSummary {
    param($Snapshot)

    Write-Host ''
    Write-Host 'OpenCode install probe' -ForegroundColor Cyan
    Write-Host ('  opencode      : ' + ($Snapshot.tooling.opencode.version ?? 'NOT ON PATH'))
    Write-Host ('  sqlite3       : ' + ($Snapshot.tooling.sqlite3.version ?? 'NOT ON PATH'))

    foreach ($role in $Snapshot.roots.Keys) {
        $r = $Snapshot.roots[$role]
        Write-Host ('  {0,-14}: {1,12:N0} bytes  {2,6} files  {3}' -f `
                $role, $r.bytes, $r.files, $r.path)
    }

    if ($Snapshot.database.present -and $Snapshot.database.inspected) {
        $nonEmpty = @($Snapshot.database.rowsByTable.Keys |
            Where-Object { $Snapshot.database.rowsByTable[$_] -gt 0 })
        # NOT `||` — in PowerShell 7 that is the pipeline-chain operator, which runs the
        # right-hand side when the left FAILS. It is not a value fallback.
        $nonEmptyText = if ($nonEmpty.Count -gt 0) { $nonEmpty -join ', ' } else { '(none)' }
        Write-Host ('  database      : {0} tables, non-empty: {1}' -f `
                $Snapshot.database.tableCount, $nonEmptyText)
    }

    if ($Snapshot.database.Contains('createdByThisProbe') -and $Snapshot.database.createdByThisProbe) {
        Write-Host '  [!] opencode.db did not exist until this probe ran - `opencode debug paths`' -ForegroundColor Yellow
        Write-Host '      initialises it. The database below is this probe''s own footprint.' -ForegroundColor Yellow
    }

    $verdict = if ($Snapshot.usage.isUsedInstall) { 'YES' } else { 'NO' }
    $colour = if ($Snapshot.usage.isUsedInstall) { 'Green' } else { 'Yellow' }
    Write-Host ('  used install  : ' + $verdict) -ForegroundColor $colour
    if ($Snapshot.usage.blocksPhase14) {
        Write-Host '  -> Phase 14 remains blocked: no session history to measure.' -ForegroundColor Yellow
    }
    Write-Host ''
}

function Invoke-ProbeMain {
    $target = $ProbeOutputPath
    if ([string]::IsNullOrWhiteSpace($target)) {
        # $PSScriptRoot is empty when this is dot-sourced from a prompt rather than run as a file.
        $base = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { (Get-Location).Path } else { $PSScriptRoot }
        $target = Join-Path (Join-Path (Join-Path $base '..') 'docs') 'opencode-install-probe.json'
    }
    $target = [System.IO.Path]::GetFullPath($target)

    $snapshot = New-ProbeSnapshot

    $json = $snapshot | ConvertTo-Json -Depth 12
    Set-Content -LiteralPath $target -Value $json -Encoding utf8NoBOM

    if (-not $ProbeQuiet) {
        Write-ProbeSummary -Snapshot $snapshot
        Write-Host ('Snapshot written: ' + (Get-ProbeRedactedPath $target))
    }
}

# Guarded on an env var, not on $MyInvocation.InvocationName: the VS Code extension dot-sources
# on F5, where InvocationName IS '.', so the conventional guard would never run Main.
if (-not $env:PROBE_OPENCODE_NOEXEC) {
    Invoke-ProbeMain
}
