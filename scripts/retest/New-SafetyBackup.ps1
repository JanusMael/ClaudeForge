#requires -Version 7
<#
    Safety backup before E3 (backup/restore), placed OUTSIDE the testing zone so
    a misbehaving restore cannot reach it.

    Excludes:
      .credentials.json  — a secret; ClaudeForge never edits it, so copying it
                           would duplicate a credential for no benefit.
      projects/ sessions/ file-history/ cache/ downloads/ session-*/
                         — bulk session transcripts and caches, not config.
#>

$ErrorActionPreference = 'Stop'

$src   = Join-Path $env:USERPROFILE '.claude'
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$dest  = Join-Path 'C:\RetestSafety' ('claude-home-' + $stamp)

$excludeDirs = @(
    'projects', 'sessions', 'file-history', 'cache', 'downloads',
    'session-env', 'session-index-backups', 'shell-snapshots', 'todos', 'statsig'
)
$excludeFiles = @('.credentials.json')

New-Item -ItemType Directory -Path $dest -Force | Out-Null

$copiedFiles = 0
$copiedDirs  = 0
$skipped     = [System.Collections.Generic.List[string]]::new()

foreach ($entry in Get-ChildItem -LiteralPath $src -Force) {
    if ($entry.PSIsContainer) {
        if ($excludeDirs -contains $entry.Name) { $skipped.Add('dir  ' + $entry.Name); continue }
        Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $dest $entry.Name) -Recurse -Force
        $copiedDirs++
    }
    else {
        if ($excludeFiles -contains $entry.Name) { $skipped.Add('file ' + $entry.Name + '  (secret, deliberately not copied)'); continue }
        Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $dest $entry.Name) -Force
        $copiedFiles++
    }
}

# ~/.claude.json lives BESIDE .claude, not inside it, and Claude Code treats it
# as config — so it belongs in a config backup.
$claudeJson = Join-Path $env:USERPROFILE '.claude.json'
if (Test-Path -LiteralPath $claudeJson) {
    Copy-Item -LiteralPath $claudeJson -Destination (Join-Path $dest '_claude.json') -Force
    $copiedFiles++
}

$size = (Get-ChildItem -LiteralPath $dest -Recurse -Force -File | Measure-Object -Property Length -Sum).Sum

Write-Host ('Backup location : ' + $dest)
Write-Host ('Directories     : ' + $copiedDirs)
Write-Host ('Files           : ' + $copiedFiles)
Write-Host ('Total size      : ' + [math]::Round($size / 1MB, 2) + ' MB')
Write-Host ''
Write-Host 'Deliberately skipped:'
foreach ($s in $skipped) { Write-Host ('  ' + $s) }
Write-Host ''
Write-Host 'Key files present in the backup:'
foreach ($k in @('settings.json', 'CLAUDE.md', '_claude.json')) {
    $p = Join-Path $dest $k
    Write-Host ('  {0,-16} {1}' -f $k, (Test-Path -LiteralPath $p))
}
