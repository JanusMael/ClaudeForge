#Requires -Version 7.0
<#
.SYNOPSIS
    Captures the table-and-column shape of a live `opencode.db` into the committed snapshot
    that the backup redactor is checked against.

.DESCRIPTION
    Phase 14 redacts secrets from a COPY of OpenCode's database rather than excluding the file,
    which keeps the user's session history. That choice is only safe while the set of
    secret-bearing columns is known — and it is upstream's schema, changed on upstream's
    schedule, with no announcement.

    So the shape is captured here, committed, and guarded three ways by
    `OpenCodeDatabaseSchemaTests`: every allow-list entry must exist, every obviously-secret
    column must be classified, and the snapshot's digest must match a constant that only moves
    when somebody deliberately moves it. Refreshing this file after an OpenCode upgrade is
    therefore supposed to redden the suite — that is the alarm working, not a chore.

    ⛔ READ-ONLY, and the reason is not obvious: opening a database whose `-wal` is present
    CHECKPOINTS it, which rewrites the user's file. The three files are copied out and every
    query runs against the copy. A capture is a read; it must not leave a write behind.

    ⓘ Only `sqlite_master` and `pragma_table_info` are ever queried. No statement selects a row,
    so this cannot capture data even by accident — which is what makes the output committable
    from a database that has a `credential` table.

.PARAMETER DbSchemaOutputPath
    Where to write the snapshot. Defaults to the committed asset in OpenCode.Sdk.

.PARAMETER DbSchemaDatabasePath
    An explicit `opencode.db`. Defaults to the install under the user profile.

.NOTES
    Set REFRESH_DB_SCHEMA_NOEXEC=1 to dot-source this for its functions without running it.
#>
[CmdletBinding()]
param(
    [string] $DbSchemaOutputPath,
    [string] $DbSchemaDatabasePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:DbSchemaSnapshotVersion = 1

function Get-DbSchemaSqlite {
    $cmd = Get-Command -Name 'sqlite3' -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $cmd) {
        throw 'sqlite3 is not on PATH. Install it (winget install SQLite.SQLite) and re-run.'
    }
    return $cmd.Source
}

function Invoke-DbSchemaQuery {
    param([string] $Sqlite3, [string] $DatabasePath, [string] $Sql)

    $raw = & $Sqlite3 '-json' $DatabasePath $Sql 2>&1
    $exit = $LASTEXITCODE
    $text = (@($raw) -join "`n").Trim()

    if ($exit -ne 0) { throw "sqlite3 exited $exit on '$Sql': $text" }
    if ([string]::IsNullOrWhiteSpace($text)) { return @() }

    return @($text | ConvertFrom-Json)
}

function Get-DbSchemaTableMap {
    <#  table -> ordered column names, for every user table. Sorted by table name so the
        committed file diffs cleanly; columns keep their declared order because that is how
        someone reading the schema expects to see them. #>
    param([string] $Sqlite3, [string] $DatabasePath)

    $tables = Invoke-DbSchemaQuery -Sqlite3 $Sqlite3 -DatabasePath $DatabasePath -Sql @'
SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;
'@

    $map = [ordered]@{}
    foreach ($t in $tables) {
        $escaped = $t.name -replace "'", "''"
        $cols = Invoke-DbSchemaQuery -Sqlite3 $Sqlite3 -DatabasePath $DatabasePath `
            -Sql "SELECT name FROM pragma_table_info('$escaped') ORDER BY cid;"
        $map[$t.name] = @($cols | ForEach-Object { $_.name })
    }

    return $map
}

function Get-DbSchemaOpenCodeVersion {
    $cmd = Get-Command -Name 'opencode' -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $cmd) { return 'unknown' }
    try {
        return ((& $cmd.Source '--version' 2>&1) | Select-Object -First 1).ToString().Trim()
    }
    catch [System.ComponentModel.Win32Exception] {
        return 'unknown'
    }
}

function Get-DbSchemaDigest {
    <#  A stable digest of the table-and-column set. The C# guard carries this as a constant, so
        any schema change reddens the suite until a human updates it — which is the whole point:
        the alarm has to be louder than a quiet file change nobody reads. #>
    param($TableMap)

    $lines = foreach ($table in $TableMap.Keys) {
        $table + ':' + (($TableMap[$table]) -join ',')
    }

    $joined = ($lines -join "`n")
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($joined)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [System.BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Invoke-DbSchemaMain {
    $sqlite = Get-DbSchemaSqlite

    $dbPath = $DbSchemaDatabasePath
    if ([string]::IsNullOrWhiteSpace($dbPath)) {
        $dbPath = Join-Path (Join-Path (Join-Path (Join-Path $HOME '.local') 'share') 'opencode') 'opencode.db'
    }
    if (-not (Test-Path -LiteralPath $dbPath)) {
        throw "No opencode.db at '$dbPath'. Run OpenCode once, or pass -DbSchemaDatabasePath."
    }

    $target = $DbSchemaOutputPath
    if ([string]::IsNullOrWhiteSpace($target)) {
        $base = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { (Get-Location).Path } else { $PSScriptRoot }
        $target = Join-Path (Join-Path (Join-Path (Join-Path $base '..') 'src') 'OpenCode.Sdk') `
            'Assets/OpenCodeDatabaseSchema.json'
    }
    $target = [System.IO.Path]::GetFullPath($target)

    # ⛔ Copy all three files, then query the copy. Opening the original would checkpoint its
    # -wal, and a capture must not write to what it is capturing.
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('dbschema-' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $work -Force | Out-Null
        foreach ($suffix in @('', '-wal', '-shm')) {
            $src = "$dbPath$suffix"
            if (Test-Path -LiteralPath $src) {
                Copy-Item -LiteralPath $src -Destination (Join-Path $work "opencode.db$suffix") -Force
            }
        }

        $copy = Join-Path $work 'opencode.db'
        $map = Get-DbSchemaTableMap -Sqlite3 $sqlite -DatabasePath $copy

        if ($map.Keys.Count -eq 0) {
            throw 'The database reported no tables. Refusing to write an empty snapshot.'
        }

        $digest = Get-DbSchemaDigest -TableMap $map

        $snapshot = [ordered]@{
            snapshotVersion  = $script:DbSchemaSnapshotVersion
            capturedAtUtc    = (Get-Date).ToUniversalTime().ToString('o')
            openCodeVersion  = Get-DbSchemaOpenCodeVersion
            tableCount       = $map.Keys.Count
            tableColumnDigest = $digest
            tables           = $map
        }

        $json = $snapshot | ConvertTo-Json -Depth 8

        New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($target)) -Force | Out-Null

        # Temp-then-replace: writing straight onto the target truncates it at open, so a throw
        # mid-write would destroy the committed snapshot.
        $tmp = $target + '.tmp'
        Set-Content -LiteralPath $tmp -Value $json -Encoding utf8NoBOM
        Move-Item -LiteralPath $tmp -Destination $target -Force

        Write-Host ''
        Write-Host 'OpenCode database schema captured' -ForegroundColor Cyan
        Write-Host ('  opencode : ' + $snapshot.openCodeVersion)
        Write-Host ('  tables   : ' + $snapshot.tableCount)
        Write-Host ('  digest   : ' + $digest)
        Write-Host ('  written  : ' + $target)
        Write-Host ''
        Write-Host 'If the digest changed, OpenCodeDatabaseSchemaTests will FAIL until the' -ForegroundColor Yellow
        Write-Host 'constant is updated deliberately. Review the diff for new secret-bearing' -ForegroundColor Yellow
        Write-Host 'columns BEFORE updating it - that review is the reason the guard exists.' -ForegroundColor Yellow
        Write-Host ''
    }
    finally {
        if (Test-Path -LiteralPath $work) {
            Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if (-not $env:REFRESH_DB_SCHEMA_NOEXEC) {
    Invoke-DbSchemaMain
}
