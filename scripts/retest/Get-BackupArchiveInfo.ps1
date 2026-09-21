#requires -Version 7
<#
    -SecretPattern scans entry CONTENTS for a literal secret (E4). Use it with a
    known planted value, never a real one.

    ⚠ "The secret does not appear" is only evidence if the surface WOULD have
    carried it. A zero count against a surface that never mentions the key at all
    is the absence of a test, not a pass — so this also reports whether the key
    NAME occurs, which tells the two apart.
#>
param(
    [Parameter(Mandatory)] [string] $Path,
    [string] $SecretPattern,
    [string] $SecretKeyName = 'ANTHROPIC_API_KEY',
    [switch] $ShowManifest
)

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

    if ($ShowManifest) {
        Write-Host ''
        Write-Host '--- manifest.json contents ---'
        $m = $zip.Entries | Where-Object { $_.FullName -eq 'manifest.json' } | Select-Object -First 1
        if ($m) {
            $sr = New-Object System.IO.StreamReader($m.Open())
            try { Write-Host $sr.ReadToEnd() } finally { $sr.Dispose() }
        } else { Write-Host '  (no manifest.json)' }
    }

    if ($SecretPattern) {
        Write-Host ''
        Write-Host '--- E4 secret scan ---'
        $withValue = [System.Collections.Generic.List[string]]::new()
        $withName  = [System.Collections.Generic.List[string]]::new()

        foreach ($e in $zip.Entries) {
            if ($e.Length -eq 0 -or $e.Length -gt 5MB) { continue }
            if ($e.FullName -notmatch '\.(json|md|txt|jsonc|yaml|yml)$') { continue }
            $sr = New-Object System.IO.StreamReader($e.Open())
            try { $text = $sr.ReadToEnd() } catch { continue } finally { $sr.Dispose() }
            if ($text -like ('*' + $SecretPattern + '*'))  { $withValue.Add($e.FullName) }
            if ($text -like ('*' + $SecretKeyName + '*'))  { $withName.Add($e.FullName) }
        }

        Write-Host ('  entries containing the KEY NAME  (' + $SecretKeyName + '): ' + $withName.Count)
        foreach ($n in ($withName  | Select-Object -First 8)) { Write-Host ('      ' + $n) }
        Write-Host ('  entries containing the RAW VALUE            : ' + $withValue.Count)
        foreach ($n in ($withValue | Select-Object -First 8)) { Write-Host ('    ⛔ ' + $n) }

        Write-Host ''
        if ($withName.Count -eq 0) {
            Write-Host '  ⚠ INCONCLUSIVE: the key NAME appears nowhere, so a zero raw-value count' -ForegroundColor Yellow
            Write-Host '    proves nothing — this surface never carried the secret to begin with.' -ForegroundColor Yellow
        }
        elseif ($withValue.Count -eq 0) {
            Write-Host '  ✅ The key name is present and the raw value is not — genuinely redacted.' -ForegroundColor Green
        }
        else {
            Write-Host '  ⛔ RAW SECRET PRESENT in the archive.' -ForegroundColor Red
        }
    }
}
finally { $zip.Dispose() }
