#Requires -Version 7.0
<#
.SYNOPSIS
    Map Avalonia-compiled `XamlClosure_N` type names back to the `.axaml` file
    (or enclosing control type) that produced them, so that IL2026 trim warnings
    of the form

        CompiledAvaloniaXaml.!AvaloniaResources.XamlClosure_13.Build_7(...)

    can be traced to the actual source XAML.

.DESCRIPTION
    Avalonia's XAML compiler emits one `XamlClosure_N` nested type per compiled
    `.axaml` resource, numbered sequentially per output assembly. The closure is
    placed either:

        * Inside the code-behind class for a `UserControl` / `Window`
          (e.g. `ClaudeForge.Views.MainWindow`), or
        * Inside `CompiledAvaloniaXaml.!AvaloniaResources` for
          `ResourceDictionary`s and `Application.Styles` styles.
          (The leading `!` is an Avalonia name-mangling prefix to avoid
          colliding with user types — not a typo.)

    THIS SCRIPT IS A BACKSTOP, NOT THE PRIMARY DIAGNOSTIC.

    Directory.Build.props sets `<DebugType>embedded</...>` for all projects in
    Release, and ClaudeForge.csproj additionally sets `<TrimmerSingleWarn>false</...>`
    and `<TrimmerRemoveSymbols>false</...>`.  Together these mean ILLink warnings
    for OUR OWN assemblies already carry the originating `.axaml` file and line
    number directly.  In that common case, this script tells you nothing the
    warning itself doesn't already say.

    Where this script still earns its keep is third-party assemblies that ship
    without PDBs — Semi.Avalonia, Avalonia.Controls.DataGrid, etc. Their
    closures appear in IL2026 warnings as bare `XamlClosure_N.Build_M` with no
    file/line info, and this tool is the only way to prove which DLL owns a
    given closure number (closure numbers are compile-order and are NOT stable
    across assemblies or versions). Use `-IncludeReferences` to scan sibling
    DLLs in the publish output.

    See TRIMMING.md at the repo root for the full diagnostic flow. The
    recommended first step when the publish warning count is non-zero is to
    grep the ILLink verbose log for `--link-attributes` (confirms the
    suppression XML reached the linker); this script is step 4 of that flow.

    The script walks the assembly's metadata directly via
    `System.Reflection.Metadata`, so it never actually *loads* the assembly —
    safe to run against trimmed / self-contained builds without side effects.

.PARAMETER Path
    One or more paths to `.dll` files to analyse. If omitted, defaults to the
    usual post-publish win-x64 output location:

        src/ClaudeForge/obj/Release/net10.0/win-x64/linked/*.dll
        src/ClaudeForge/obj/Release/net10.0/win-x64/*.dll

    Wildcards are accepted.

.PARAMETER WarningsPath
    Optional path to a publish log file. If supplied, the script parses every
    IL2026 `CompiledAvaloniaXaml.!AvaloniaResources.XamlClosure_N.Build_M` line
    out of the log and joins it to the closure map, so you get a compact
    "warning → XAML file" table instead of raw IL2026 lines.

.PARAMETER IncludeReferences
    When true, also scans every `.dll` next to the input assembly (typical for
    a trimmed self-contained publish) so that closures emitted into
    `Semi.Avalonia.dll` or other third-party theme assemblies are resolved too.

.EXAMPLE
    # Plain map of every closure in the app assembly.
    pwsh ./src/publish/Analyze-XamlClosures.ps1

.EXAMPLE
    # Correlate a full publish log with closure locations.
    pwsh ./src/publish/Analyze-XamlClosures.ps1 `
        -WarningsPath .\last-publish.log `
        -IncludeReferences

.EXAMPLE
    # Analyse a specific RID's linked output.
    pwsh ./src/publish/Analyze-XamlClosures.ps1 `
        -Path src/ClaudeForge/obj/Release/net10.0/osx-arm64/linked/ClaudeForge.dll

.NOTES
    Requires PowerShell 7+ (`System.Reflection.Metadata` is net8+ in-box on
    pwsh 7). On Windows PowerShell 5.1 the `PEReaderExtensions` type is
    unavailable and the script will fail.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string[]] $Path,

    [Parameter()]
    [string] $WarningsPath,

    [Parameter()]
    [switch] $IncludeReferences
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Resolve default input paths relative to the script location --------------
# The script lives in src/publish/, so two Split-Path -Parent calls reach the
# repo root:  src/publish/ → src/ → repo root.
$srcRoot  = Split-Path -Parent $PSScriptRoot           # src/publish/ → src/
$repoRoot = if ($srcRoot) { Split-Path -Parent $srcRoot } else { (Get-Location).Path }
if (-not $repoRoot) { $repoRoot = (Get-Location).Path }
$appObjRoot = Join-Path $repoRoot 'src/ClaudeForge/obj/Release/net10.0'

if (-not $Path -or $Path.Count -eq 0) {
    $candidates = @()
    if (Test-Path $appObjRoot) {
        # Prefer the linked (post-trim) assembly because its metadata reflects
        # what ILLink actually analysed when it produced the warnings. Fall back
        # to the pre-link assembly if the linked one isn't built.
        $candidates = Get-ChildItem -Path $appObjRoot -Recurse -File -Filter 'ClaudeForge.dll' `
            -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\linked\\' -or $_.FullName -match 'net10\.0\\win-x64\\ClaudeForge\.dll$' } |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $candidates) {
        throw "No default input path found. Build Release once (e.g. via src/publish.ps1) or pass -Path explicitly."
    }
    $Path = @($candidates)
}

# Expand wildcards into concrete files, silently dropping nonexistent matches.
$resolvedInputs = @()
foreach ($p in $Path) {
    if (Test-Path $p -PathType Leaf) { $resolvedInputs += (Resolve-Path $p).Path; continue }
    $matched = Get-ChildItem -Path $p -ErrorAction SilentlyContinue | Where-Object { -not $_.PSIsContainer }
    if ($matched) { $resolvedInputs += $matched.FullName }
}

if ($IncludeReferences) {
    # Add every sibling .dll of each input so third-party assemblies (Semi.Avalonia, etc.)
    # get scanned too. This is the common case: a XamlClosure_N in an ILLink
    # warning may live in a referenced theme, not our own code.
    $dirs = $resolvedInputs | ForEach-Object { Split-Path $_ -Parent } | Sort-Object -Unique
    foreach ($dir in $dirs) {
        $resolvedInputs += (Get-ChildItem -Path $dir -Filter '*.dll' -File).FullName
    }
    $resolvedInputs = $resolvedInputs | Sort-Object -Unique
}

if (-not $resolvedInputs) {
    throw "No assemblies resolved from -Path $($Path -join ', ')"
}

Write-Host ("Scanning {0} assembl{1} for XamlClosure_N types..." -f $resolvedInputs.Count, $(if ($resolvedInputs.Count -eq 1) { 'y' } else { 'ies' })) -ForegroundColor Cyan

# --- Metadata-only type enumeration -----------------------------------------
# Loading System.Reflection.Metadata by assembly name works in pwsh 7+ because
# the runtime already ships it; on Windows PowerShell 5.1 this would throw,
# which is why the `#Requires -Version 7.0` guard is at the top of the file.
Add-Type -AssemblyName 'System.Reflection.Metadata' | Out-Null

function Get-XamlClosures {
    param([string] $DllPath)

    $stream = [System.IO.File]::OpenRead($DllPath)
    try {
        $pe = New-Object System.Reflection.PortableExecutable.PEReader($stream)
        if (-not $pe.HasMetadata) { return @() }
        $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)

        $asmName = $md.GetString($md.GetAssemblyDefinition().Name)
        $rows = @()
        foreach ($th in $md.TypeDefinitions) {
            $td   = $md.GetTypeDefinition($th)
            $name = $md.GetString($td.Name)
            if ($name -notlike 'XamlClosure_*') { continue }

            # The closure is always nested; the declaring type tells us which
            # control/window/resource dictionary it belongs to.
            $decl       = $td.GetDeclaringType()
            $enclosing  = '<root>'
            if (-not $decl.IsNil) {
                $dt      = $md.GetTypeDefinition($decl)
                $encNs   = $md.GetString($dt.Namespace)
                $encName = $md.GetString($dt.Name)
                $enclosing = if ($encNs) { "$encNs.$encName" } else { $encName }
            }

            # Also emit each Build_M method name; ILLink warnings identify the
            # specific closure method, so listing them here helps correlate a
            # warning like "XamlClosure_13.Build_7" with a callable entry point.
            $methods = foreach ($mh in $td.GetMethods()) {
                $md.GetString($md.GetMethodDefinition($mh).Name)
            }

            $rows += [pscustomobject]@{
                Assembly  = $asmName
                Closure   = $name
                Enclosing = $enclosing
                Methods   = ($methods -join ', ')
            }
        }
        return $rows
    } finally {
        if ($pe)     { $pe.Dispose() }
        if ($stream) { $stream.Dispose() }
    }
}

$all = @()
foreach ($dll in $resolvedInputs) {
    try {
        $all += Get-XamlClosures -DllPath $dll
    } catch {
        Write-Warning ("Skipping {0}: {1}" -f (Split-Path $dll -Leaf), $_.Exception.Message)
    }
}

if (-not $all) {
    Write-Host "No XamlClosure_N types found in any scanned assembly." -ForegroundColor Yellow
    return
}

# --- Output (plain map or log-correlated report) -----------------------------
if (-not $WarningsPath) {
    Write-Host ''
    Write-Host 'Closure -> enclosing type' -ForegroundColor Green
    $all | Sort-Object Assembly, {
        # Natural-sort: "XamlClosure_2" before "XamlClosure_10".
        [int]($_.Closure -replace 'XamlClosure_','')
    } | Format-Table -AutoSize Assembly, Closure, Enclosing
    return
}

if (-not (Test-Path $WarningsPath)) {
    throw "Warnings log not found: $WarningsPath"
}

# Parse IL2026 warnings of the specific shape emitted by ILLink for Avalonia
# compiled XAML. We only care about the closure + build-method identifier;
# duplicate lines are collapsed into a count column so the final report
# summarises "N warnings in XamlClosure_X.Build_Y → SomeView.axaml".
$warnings = Select-String -Path $WarningsPath -Pattern 'XamlClosure_(\d+)\.Build_(\d+)' `
    | ForEach-Object {
        $m = [regex]::Match($_.Line, 'XamlClosure_(\d+)\.Build_(\d+)')
        if ($m.Success) {
            [pscustomobject]@{
                Closure = "XamlClosure_$($m.Groups[1].Value)"
                Method  = "Build_$($m.Groups[2].Value)"
            }
        }
    }

if (-not $warnings) {
    Write-Host "No XamlClosure_N IL2026 lines found in log $WarningsPath." -ForegroundColor Yellow
    return
}

$grouped = $warnings | Group-Object Closure, Method | ForEach-Object {
    $first = $_.Group[0]
    $match = $all | Where-Object { $_.Closure -eq $first.Closure } | Select-Object -First 1
    [pscustomobject]@{
        Count     = $_.Count
        Closure   = $first.Closure
        Method    = $first.Method
        Assembly  = if ($match) { $match.Assembly }  else { '<unresolved>' }
        Enclosing = if ($match) { $match.Enclosing } else { '<unresolved>' }
    }
}

Write-Host ''
Write-Host 'IL2026 warning counts by source XAML:' -ForegroundColor Green
$grouped | Sort-Object Assembly, Closure, Method | Format-Table -AutoSize Count, Assembly, Enclosing, Closure, Method

$unresolved = $grouped | Where-Object { $_.Enclosing -eq '<unresolved>' }
if ($unresolved) {
    Write-Host ''
    Write-Warning "$($unresolved.Count) closure(s) could not be resolved. Re-run with -IncludeReferences to scan sibling assemblies (Semi.Avalonia etc.)."
}
