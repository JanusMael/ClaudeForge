#requires -Version 7
param([Parameter(Mandatory)] [string] $Path)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
try {
    Write-Host ('Entries: ' + $zip.Entries.Count)
    Write-Host ''

    Write-Host '--- top-level roots ---'
    $roots = @{}
    foreach ($e in $zip.Entries) {
        $first = ($e.FullName -split '/')[0]
        if (-not $roots.ContainsKey($first)) { $roots[$first] = [pscustomobject]@{ Count = 0; Bytes = [long]0 } }
        $roots[$first].Count++
        $roots[$first].Bytes += $e.Length
    }
    foreach ($k in ($roots.Keys | Sort-Object)) {
        Write-Host ('  {0,-40} files={1,-7} {2} MB' -f $k, $roots[$k].Count, [math]::Round($roots[$k].Bytes / 1MB, 2))
    }

    Write-Host ''
    Write-Host '--- manifest / metadata entries ---'
    foreach ($e in $zip.Entries) {
        if ($e.FullName -match '(manifest|metadata|\.json)$' -and ($e.FullName -split '/').Count -le 2) {
            Write-Host ('  ' + $e.FullName + '  (' + $e.Length + ' bytes)')
        }
    }

    Write-Host ''
    Write-Host '--- does it contain credentials? (must be NO) ---'
    $cred = @($zip.Entries | Where-Object { $_.FullName -like '*credentials*' })
    if ($cred.Count -eq 0) { Write-Host '  none ✓' }
    else { foreach ($c in $cred) { Write-Host ('  ⛔ ' + $c.FullName) } }

    Write-Host ''
    Write-Host '--- does it contain the scratch project settings? ---'
    $scratch = @($zip.Entries | Where-Object { $_.FullName -like '*retest-2026.3.917*' })
    Write-Host ('  entries mentioning the scratch project: ' + $scratch.Count)
    foreach ($s in ($scratch | Select-Object -First 10)) { Write-Host ('    ' + $s.FullName) }
}
finally { $zip.Dispose() }
