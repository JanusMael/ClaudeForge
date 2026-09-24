#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Compares the test identities recorded in two sets of TRX files and names every difference.

.DESCRIPTION
    Written for plans/00006 (MSTest -> xUnit v3), where the proof of the move is that the name
    set does not change. A test's identity is its ASSEMBLY, its CLASS (fully qualified) and its
    METHOD, read from each TRX's TestDefinitions rather than from the display name, because the two
    frameworks format display names differently. Data-driven rows are compared by COUNT per method:
    MSTest and xUnit name rows differently, but a dropped row changes the count, and the count is
    reported against the method that lost it.

    Outcomes are compared too. A test that was Passed and is now NotExecuted (skipped), or the
    reverse, is listed, so every intended Inconclusive -> Skip is accounted for by name.

    Exits 0 when the two sets are identical, 1 when anything differs, 2 on bad input.

.PARAMETER Baseline
    A directory of .trx files (searched recursively), or one .trx file.

.PARAMETER Candidate
    The same, for the run being compared against the baseline.

.PARAMETER Out
    Optional path for the report. It is always written to the output stream as well.

.EXAMPLE
    pwsh -NoProfile -File scripts/Compare-TestNames.ps1 -Baseline artifacts/xunit-move/baseline -Candidate artifacts/xunit-move/step1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Candidate,
    [string] $Out
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-TrxFiles([string] $path) {
    if (Test-Path -LiteralPath $path -PathType Leaf) { return Get-Item -LiteralPath $path }
    if (Test-Path -LiteralPath $path -PathType Container) {
        return Get-ChildItem -LiteralPath $path -Recurse -File -Filter '*.trx'
    }
    throw "Not a .trx file or a directory: $path"
}

# Identity -> @{ Rows = <int>; Outcomes = <sorted outcome list> }, keyed 'Assembly|Class|Method'.
function Read-TestSet([string] $path) {
    $files = @(Get-TrxFiles $path)
    if ($files.Count -eq 0) { throw "No .trx files under $path" }

    $set = @{}
    foreach ($file in $files) {
        [xml] $trx = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8
        $ns = New-Object System.Xml.XmlNamespaceManager $trx.NameTable
        $ns.AddNamespace('t', $trx.DocumentElement.NamespaceURI)

        $definitions = @{}
        foreach ($unitTest in $trx.SelectNodes('//t:TestDefinitions/t:UnitTest', $ns)) {
            $method = $unitTest.SelectSingleNode('t:TestMethod', $ns)
            $assembly = [System.IO.Path]::GetFileNameWithoutExtension($method.GetAttribute('codeBase'))
            $className = $method.GetAttribute('className')
            # ⚠ The frameworks spell the METHOD differently: MSTest writes the bare name, xUnit v3
            # writes 'Namespace.Class.Method' and, for a theory row, appends '(arg: value)'.
            # Normalise both to the bare name so a moved test keeps its identity.
            $name = $method.GetAttribute('name')
            if ($name.StartsWith($className + '.', [System.StringComparison]::Ordinal)) {
                $name = $name.Substring($className.Length + 1)
            }
            $paren = $name.IndexOf('(')
            if ($paren -gt 0) { $name = $name.Substring(0, $paren) }
            $definitions[$unitTest.GetAttribute('id')] = $assembly + '|' + $className + '|' + $name
        }

        foreach ($result in $trx.SelectNodes('//t:Results/t:UnitTestResult', $ns)) {
            $key = $definitions[$result.GetAttribute('testId')]
            if ($null -eq $key) { throw "Result with no definition in $($file.FullName): $($result.GetAttribute('testName'))" }
            if (-not $set.ContainsKey($key)) { $set[$key] = [System.Collections.Generic.List[string]]::new() }
            $set[$key].Add($result.GetAttribute('outcome'))
        }
    }
    return $set
}

function Format-Outcomes([System.Collections.Generic.List[string]] $outcomes) {
    return ($outcomes | Group-Object | Sort-Object Name | ForEach-Object { $_.Name + ' x' + $_.Count }) -join ', '
}

try {
    $before = Read-TestSet $Baseline
    $after = Read-TestSet $Candidate
} catch {
    Write-Error $_
    exit 2
}

$lines = [System.Collections.Generic.List[string]]::new()
$differences = 0

foreach ($key in @($before.Keys + $after.Keys | Sort-Object -Unique)) {
    $a = $before[$key]
    $b = $after[$key]
    if ($null -eq $b) {
        $lines.Add('REMOVED  ' + $key + '  (' + (Format-Outcomes $a) + ')')
        $differences++
    } elseif ($null -eq $a) {
        $lines.Add('ADDED    ' + $key + '  (' + (Format-Outcomes $b) + ')')
        $differences++
    } elseif ($a.Count -ne $b.Count) {
        $lines.Add('ROWS     ' + $key + '  ' + $a.Count + ' -> ' + $b.Count)
        $differences++
    } elseif ((Format-Outcomes $a) -ne (Format-Outcomes $b)) {
        $lines.Add('OUTCOME  ' + $key + '  ' + (Format-Outcomes $a) + ' -> ' + (Format-Outcomes $b))
        $differences++
    }
}

function Get-Totals([hashtable] $set) {
    $all = @($set.Values | ForEach-Object { $_ })
    $assemblies = @($set.Keys | ForEach-Object { $_.Split('|')[0] } | Sort-Object -Unique)
    return 'assemblies ' + $assemblies.Count + ', methods ' + $set.Count + ', results ' + $all.Count +
        ', skipped ' + @($all | Where-Object { $_ -eq 'NotExecuted' }).Count
}

$lines.Insert(0, 'baseline:  ' + (Get-Totals $before))
$lines.Insert(1, 'candidate: ' + (Get-Totals $after))
$lines.Insert(2, 'differences: ' + $differences)

$lines | Write-Output
if ($Out) { Set-Content -LiteralPath $Out -Value $lines -Encoding utf8 }
exit ([int]($differences -ne 0))
